using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ESRI.ArcGIS.Client;
using ESRI.ArcGIS.Client.Extensibility;
using ESRI.ArcGIS.Client.Geometry;
using ESRI.ArcGIS.Client.Symbols;
using ESRI.ArcGIS.Client.Tasks;
using ESRI.ArcGIS.Client.Toolkit;
using ESRI.ArcGIS.Client.FeatureService;
using System.Text.RegularExpressions;

using System.Windows.Browser;

namespace QueryRelatedRecords.AddIns
{
    [Export(typeof(ICommand))]
    [DisplayName("Query Related Records")]
    [Description("Query related records for a feature")]
    [Category("Query")]
    [DefaultIcon("/ESRI.ArcGIS.Mapping.Controls;component/images/icon_tools16.png")]
    public class QueryRelatedTool : ICommand
    {
        public QueryRelatedTool()
        {
            // Initialize the QueryTask
            queryTask = new QueryTask();
            queryTask.ExecuteRelationshipQueryCompleted += QueryTask_ExecuteRelationshipQueryCompleted;
            queryTask.Failed += QueryTask_Failed;
            relationsListForm = new VShortList(this);
        }

        #region Member Variables

        private Graphic inputFeature; // Feature to get related records for.
        private QueryTask queryTask; // Query task for querying related records.
        private OnClickPopupInfo popupInfo; // Information about the feature that was clicked.
        private FeatureLayer relatesLayer; // The layer to query for related features.
        private FeatureLayer resultsLayer; // The layer containing the related features
        private RelationshipResult queryResult; // The results from the Query task.
        private const string CONTAINER_NAME = "FeatureDataGridContainer"; // Provides access to the Attribute table in the layout.
        private BusyIndicator indicator; // The busy indicator to show while the Query task is in progress.
        private Grid attributeGrid; // Attribute grid in the pop-up. The busy indicator is added to this attribute grid.
        private string objectID; // The name of the ObjectID field in the layer.
        public VShortList relationsListForm; // form for choose desired relation
        private VUtils.ArcGIS.SLViewer.VRelationInfo relationInfo; // relation id, name, table id

        #endregion

        #region ICommand members

        /// <summary>
        /// Executes the relationship query against the layer.
        /// </summary>
        /// <param name="parameter">The OnClickPopupInfo from the layer.</param>
        public void Execute(object parameter)
        {
            try {
                doExecute(parameter);
            } catch(Exception ex) {
                log(string.Format("Execute, catch error '{0}'", ex.Message));
                MessageBox.Show(string.Format(
                    "Can't show related records \n {0}", ex.Message));
            }
        } // public void Execute(object parameter)


        /// <summary>
        /// Executes the relationship query against the layer.
        /// </summary>
        /// <param name="parameter">The OnClickPopupInfo from the layer.</param>
        public void doExecute(object parameter) {
            // The plan is:
            // Get the featurelayer and clicked feature from the pop-up.
            // The PopupItem property of OnClickPopupInfo provides information
            // about the item currently shown in the pop-up.
            // Then get feature ID value and put it into ExecuteRelationshipQueryAsync task.
            // Then get related records ID's and create FeatureLayer from
            // related table/feature class, filtered by that ID's.
            // Then show grid for that layer.
            popupInfo = parameter as OnClickPopupInfo;
            inputFeature = popupInfo.PopupItem.Graphic;
            var lyr = new VUtils.ArcGIS.SLViewer.VLayer(popupInfo.PopupItem.Layer);

            // print layer info to console
            log(string.Format(
                "Execute, layer type '{0}', popupInd '{1}', popupDescr '{2}', lyrID '{3}', lyrName '{4}', title '{5}'",
                popupInfo.PopupItem.Layer.GetType(),
                popupInfo.SelectedIndex, popupInfo.SelectionDescription,
                popupInfo.PopupItem.LayerId, popupInfo.PopupItem.LayerName, popupInfo.PopupItem.Title));
            log(string.Format("Execute, lyrType '{0}', lyrUrl '{1}'", lyr.lyrType, lyr.lyrUrl));
            log(string.Format("Execute, inputFeature.Attributes.Count '{0}'", inputFeature.Attributes.Count));

            // we need FeatureLayer
            if(lyr.lyrType == "FeatureLayer") {
                // The layer to get related records for.
                // This is used to get the RelationshipID and Query url.
                relatesLayer = lyr.lyr as FeatureLayer;
            }
            else if(lyr.lyrType == "ArcGISDynamicMapServiceLayer") {
                var rLyr = getSubLayer(lyr, popupInfo.PopupItem.LayerId) as FeatureLayer;
                if(relatesLayer != null && relatesLayer.Url == rLyr.Url) {
                    // we're here after relatesLayer.Initialized
                    ;
                }
                else {
                    // init new FeatureLayer
                    relatesLayer = rLyr;
                    relatesLayer.Initialized += (a, b) => {
                        if(relatesLayer.InitializationFailure == null) {
                            var info = relatesLayer.LayerInfo;
                            log(string.Format(
                                "Execute, relatesLayer.InitializationFailure == null, info '{0}'",
                                info));
                            Execute(parameter);
                        }
                    }; // callback
                    relatesLayer.Initialize();
                    log(string.Format("Execute, relatesLayer.Initialize called, wait..."));
                    return;
                } // init new FeatureLayer
            } // if(lyr.lyrType == "ArcGISDynamicMapServiceLayer")
            else {
                throw new Exception("Layer type must be FeatureLayer or ArcGISDynamicMapServiceLayer");
            }

            // we have inited FeatureLayer now
            if(relatesLayer.LayerInfo == null) {
                throw new Exception(string.Format("Execute, relatesLayer.LayerInfo == null"));
            }
            var clickedLayer = new VUtils.ArcGIS.SLViewer.VLayer(relatesLayer);
            // check FeatureLayer info
            log(string.Format(
                "Execute, relatesLayer lyrType '{0}', lyrUrl '{1}'",
                clickedLayer.lyrType, clickedLayer.lyrUrl));

            // get relationship id
            var rels = relatesLayer.LayerInfo.Relationships;
            if(rels.Count() <= 0) {
                log(string.Format("Execute, relationships.count <= 0"));
                throw new Exception(string.Format("Layer have not relations"));
            }
            else if(rels.Count() > 1) {
                log(string.Format("Execute, relationships.count > 1"));
                if(relationsListForm.listBox1.Items.Count > 0) {
                    // continue after user input
                    // user selected relID already
                    relationInfo = new VUtils.ArcGIS.SLViewer.VRelationInfo(
                        relationsListForm.listBox1.SelectedItem as string);
                    relationsListForm.listBox1.Items.Clear();
                }
                else {
                    // new query
                    foreach(var r in rels) {
                        var ri = new VUtils.ArcGIS.SLViewer.VRelationInfo(r);
                        relationsListForm.listBox1.Items.Add(ri.descr);
                    }
                    relationsListForm.listBox1.SelectedItem = relationsListForm.listBox1.Items.First();
                    MapApplication.Current.ShowWindow("Relations",
                        relationsListForm,
                        false, // ismodal
                        (sender, canceleventargs) => {
                            log("relationsListForm onhidINGhandler");
                        }, // onhidinghandler
                        (sender, eventargs) => {
                            log("relationsListForm onhidEhandler");
                            if(relationsListForm.listBox1.SelectedItem != null) Execute(parameter);
                        }, // onhidehandler
                        WindowType.Floating
                    );
                    return; // wait for user input
                } // new query
            } // rels.count > 1
            else { // rels.count == 1
                log(string.Format("Execute, relationships.count = 1"));
                relationInfo = new VUtils.ArcGIS.SLViewer.VRelationInfo(rels.First());
            }

            // ok, we get relation info now
            log(string.Format(
                "Execute, getrelid, relationshipID '{0}', rels.count '{1}'",
                relationInfo.id, rels.Count()));

            // Get the name of the ObjectID field.
            objectID = relatesLayer.LayerInfo.ObjectIdField;
            string objectIDAlias = clickedLayer.getFieldAlias(objectID);
            log(string.Format("Execute, objectID '{0}', alias '{1}'", objectID, objectIDAlias));
            if(objectIDAlias != "") objectID = objectIDAlias; // because of bug? in Graphic.Attributes[fieldname]

            // get key value
            Object v = null;
            v = inputFeature.Attributes[objectID];
            log(string.Format("Execute, objIdValue.str='{0}'", v));
            int objIdValue = -1;
            try {
                objIdValue = Int32.Parse(string.Format("{0}", v));
            }
            catch(Exception ex) {
                // fieldname = 'OBJECTID' but alias = 'Object ID'
                var ks = string.Join(", ", inputFeature.Attributes.Keys);
                var vs = string.Join(", ", inputFeature.Attributes.Values);
                log(string.Format("Execute, inputFeature.AttributesKeys='{0}', values='{1}'", ks, vs));
                throw new Exception(string.Format("OBJECTID is not an integer"));
            }
            log(string.Format("Execute, objIdValue.int='{0}'", objIdValue));

            // Input parameters for QueryTask
            RelationshipParameter relationshipParameters = new RelationshipParameter() {
                ObjectIds = new int[] { objIdValue },
                OutFields = new string[] { "*" }, // Return all fields
                ReturnGeometry = true, // Return the geometry
                // so that features can be displayed on the map if applicable

                RelationshipId = relationInfo.id, // Obtain the desired RelationshipID
                // from the Service Details page. Here it takes the first relationship
                // it finds if there is more than one.

                OutSpatialReference = MapApplication.Current.Map.SpatialReference
            };

            // Specify the Feature Service url for the QueryTask.
            queryTask.Url = relatesLayer.Url;

            //  Execute the Query Task with specified parameters
            queryTask.ExecuteRelationshipQueryAsync(relationshipParameters);

            // Find the attribute grid in the Pop-up and insert the BusyIndicator
            attributeGrid = Utils.FindChildOfType<Grid>(popupInfo.AttributeContainer, 3);
            indicator = new BusyIndicator();
            if(attributeGrid != null) {
                // Add the Busy Indicator
                attributeGrid.Children.Add(indicator);
                indicator.IsBusy = true;
            }

            log(string.Format("Execute, completed, wait for QueryTask_ExecuteRelationshipQueryCompleted"));
        } // public void doExecute(object parameter)


        /// <summary>
        /// Checks whether the Query Related Records tool can be used.
        /// </summary>
        /// <param name="parameter">The OnClickPopupInfo from the layer.</param>
        public bool CanExecute(object parameter)
        {
            popupInfo = parameter as OnClickPopupInfo;

            return popupInfo != null && popupInfo.PopupItem != null
                //&& popupInfo.PopupItem.Layer is FeatureLayer
                //&& ((FeatureLayer)popupInfo.PopupItem.Layer).LayerInfo != null
                //&& ((FeatureLayer)popupInfo.PopupItem.Layer).LayerInfo.Relationships != null
                //&& ((FeatureLayer)popupInfo.PopupItem.Layer).LayerInfo.Relationships.Count() > 0
                && MapApplication.Current != null && MapApplication.Current.Map != null
                && MapApplication.Current.Map.Layers != null
                && MapApplication.Current.Map.Layers.Contains(popupInfo.PopupItem.Layer)
                && popupInfo.PopupItem.Graphic != null;

        } // public bool CanExecute(object parameter)

        public event EventHandler CanExecuteChanged;

        #endregion

        #region Event Handlers

        /// <summary>
        /// Handle successful query task and create FeatureLayer from related table/FC
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void QueryTask_ExecuteRelationshipQueryCompleted(
            object sender, RelationshipEventArgs e)
        {
            // queryResult used once the results layer is initialized
            queryResult = e.Result;

            // Create a new url for the related table using the querytaskUrl and the RelatedTableId.
            string resultsUrl = relatesLayer.Url;
            string[] splitResultUrl = resultsUrl.Split('/');
            resultsUrl = resultsUrl.Replace(splitResultUrl.Last<string>(), "");
            resultsUrl = resultsUrl.Insert(resultsUrl.Length, relationInfo.tableId.ToString());
            log(string.Format("QueryTask_ExecuteRelationshipQueryCompleted, resultsUrl='{0}'", resultsUrl));

            // Create a FeatureLayer for the results based on the url of the related records.
            resultsLayer = new FeatureLayer()
            {
                Url = resultsUrl
            };
            resultsLayer.OutFields.Add("*");

            // Initialize the resultsLayer to populate layer metadata (LayerInfo) so the OID field can be retrieved.
            resultsLayer.Initialized += resultsLayer_Initialized;
            resultsLayer.Initialize();

            log(string.Format("QueryTask_ExecuteRelationshipQueryCompleted, completed, wait for resultsLayer_Initialized"));
        } // private void QueryTask_ExecuteRelationshipQueryCompleted(object sender, RelationshipEventArgs e)


        private void resultsLayer_Initialized(object sender, EventArgs e)
        {
            try {
                doResultsLayer_Initialized(sender, e);
            }
            catch(Exception ex) {
                log(string.Format("resultsLayer_Initialized, error {0}", ex.Message));
                MessageBox.Show(string.Format("Can't show related records \n {0}", ex.Message));
                ClosePopup();
            }
        } // private void resultsLayer_Initialized(object sender, EventArgs e)


        /// <summary>
        /// Get related records ID's from query result, add filtered by ID's layer to map
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void doResultsLayer_Initialized(object sender, EventArgs e) {
            // Get the FeatureLayer's OID field off the LayerInfo
            string oidField = resultsLayer.LayerInfo.ObjectIdField;
            var reslyr = new VUtils.ArcGIS.SLViewer.VLayer(resultsLayer);
            string oidFieldAlias = reslyr.getFieldAlias(oidField);
            log(string.Format(
                "doResultsLayer_Initialized, resultsLayer.oidfield='{0}', alias='{1}'",
                oidField, oidFieldAlias));
            if(oidFieldAlias != "") oidField = oidFieldAlias;

            // Create a List to hold the ObjectIds
            List<int> list = new List<int>();
            IEnumerable<Graphic> RelatedRecords;

            //Go through the RelatedRecordsGroup and add the Graphic to the IEnumerable<Graphic>
            foreach(var records in queryResult.RelatedRecordsGroup) {
                RelatedRecords = records.Value;
                foreach(Graphic graphic in RelatedRecords)
                    list.Add((int)graphic.Attributes[oidField]);
            }
            log(string.Format("doResultsLayer_Initialized, relatedRecords.oidList.Count='{0}'", list.Count));
            if(list.Count <= 0) {
                throw new Exception("Haven't related records for that object");
            }

            int[] objectIDs = list.ToArray();
            resultsLayer.ObjectIDs = objectIDs;
            log(string.Format("doResultsLayer_Initialized, ID's set"));

            // Specify renderers for Point, Polyline, and Polygon features if the related features have geometry.
            if(resultsLayer.LayerInfo.GeometryType == GeometryType.Point) {
                log(string.Format("doResultsLayer_Initialized, MapPoint"));
                resultsLayer.Renderer = new SimpleRenderer() {
                    Symbol = new SimpleMarkerSymbol() {
                        Style = SimpleMarkerSymbol.SimpleMarkerStyle.Circle
                    }
                };
            }
            else if(resultsLayer.LayerInfo.GeometryType == GeometryType.Polyline) {
                log(string.Format("doResultsLayer_Initialized, Polyline"));
                resultsLayer.Renderer = new SimpleRenderer() {
                    Symbol = new SimpleLineSymbol() {
                        Color = new SolidColorBrush(Colors.Red),
                        Width = 2
                    }
                };
            }
            else if(resultsLayer.LayerInfo.GeometryType == GeometryType.Polygon) {
                log(string.Format("doResultsLayer_Initialized, Polygon"));
                resultsLayer.Renderer = new SimpleRenderer() {
                    Symbol = new SimpleFillSymbol() {
                        Fill = new SolidColorBrush(Color.FromArgb(125, 255, 0, 0)),
                        BorderBrush = new SolidColorBrush(Colors.Red)
                    }
                };
            }
            log(string.Format("doResultsLayer_Initialized, resultsLayer.Geometry is '{0}'", resultsLayer.LayerInfo.GeometryType));

            // Specify a layer name so that it displays on the Attribute table,
            // but do not display the layer in the Map Contents.
            string mapLayerName = resultsLayer.LayerInfo.Name +
                ", related records '" +
                relationInfo.name +
                "' for OID " +
                inputFeature.Attributes[objectID].ToString();
            MapApplication.SetLayerName(resultsLayer, mapLayerName);
            LayerProperties.SetIsVisibleInMapContents(resultsLayer, true);
            log(string.Format("doResultsLayer_Initialized, SetLayerName '{0}'", mapLayerName));

            // Add the layer to the map and set it as the selected layer so the attributes appear in the Attribute table.
            resultsLayer.UpdateCompleted += resultsLayer_UpdateCompleted;
            MapApplication.Current.Map.Layers.Add(resultsLayer);
        } // private void doResultsLayer_Initialized(object sender, EventArgs e)


        /// <summary>
        /// Raised when the results layer is updated. Dispalys the attribute table and closes the pop-up window.
        /// </summary>
        private void resultsLayer_UpdateCompleted(object sender, EventArgs e)
        {
            // Display the Attribute table and close the pop-up.
            MapApplication.Current.SelectedLayer = resultsLayer;
            ShowFeatureDataGrid();
            ClosePopup();
        }

        /// <summary>
        /// Handles failed QueryTask by displaying an error message.
        /// </summary>
        private void QueryTask_Failed(object sender, TaskFailedEventArgs e)
        {
            // Show failure error message
            TextBlock failureText = new TextBlock()
            {
                Margin = new Thickness(10),
                Text = e.Error.Message
            };
            MapApplication.Current.ShowWindow("Error", failureText, true);
        }


        #endregion


        #region Private Methods

        /// <summary>
        /// Return FeatureLayer from ArcGISDynamicMapServiceLayer by lyrID
        /// </summary>
        /// <param name="lyr"></param>
        /// <param name="lyrID"></param>
        /// <returns></returns>
        private Layer getSubLayer(VUtils.ArcGIS.SLViewer.VLayer lyr, int lyrID) {
            var fl = lyr.getSubLayer(lyrID);
            if(fl == null)
                throw new Exception("Can't get FeatureLayer from ArcGISDynamicMapServiceLayer");
            return fl.lyr;
        } // private Layer getSubLayer(VUtils.ArcGIS.SLViewer.VLayer lyr, int lyrID)


        /// <summary>
        /// Displays the attribute table.
        /// </summary>
        private void ShowFeatureDataGrid()
        {
            // Get the attribute table container
            FrameworkElement container = MapApplication.Current.FindObjectInLayout(CONTAINER_NAME)
                as FrameworkElement;

            if (container != null)
            {
                // try to get storyboard (animation) for showing attribute table
                Storyboard showStoryboard = container.FindStoryboard(CONTAINER_NAME + "_Show");
                if (showStoryboard != null)
                    showStoryboard.Begin(); // use storyboard if available
                else
                    container.Visibility = Visibility.Visible; // no storyboard, so set visibility directly
            }
        }

        /// <summary>
        /// Closes the Pop-up window.
        /// </summary>
        private void ClosePopup()
        {
            // Remove the indicator from the pop-up so it doesn't display the next
            // time the pop-up is opened.
            attributeGrid.Children.Remove(indicator);

            // Close the pop-up window
            InfoWindow popupWindow = popupInfo.Container as InfoWindow;
            //popupWindow.IsOpen = false;
        }

        #endregion

        public void log(String txt) {
            DateTime dt = DateTime.Now;
            var msg = string.Format("{0} QueryRelatedTool {1}\n", dt.ToString("yyyy-MM-dd hh:mm:ss"), txt);
            msg.clog();
            System.Diagnostics.Debug.WriteLine(txt);
        } // public void log(String txt)

    } // public class QueryRelatedTool : ICommand


    /// <summary>
    /// log to browser console (ie8 script console or FF firebug)
    /// </summary>
    public static class VExtClass {
        // http://kodierer.blogspot.com/2009/05/silverlight-logging-extension-method.html

        /// <summary>
        /// if you are using Firefox with the Firebug add-on or
        /// Internet Explorer 8: Use the console.log mechanism
        /// </summary>
        /// <param name="obj"></param>
        public static void clog(this object obj) {
            VUtils.ArcGIS.SLViewer.VExtClass.clog(obj);
        } // public static void clog(this object obj)

    } // public static class VExtClass

} // namespace QueryRelatedRecords.AddIns
