// Copyright (C) 1996-2012, ALGIS LLC
// Originally by Valik <vasnake@gmail.com>, 2010
//
//    This file is part of Valik code library.
//
//    This lib is free software: you can redistribute it and/or modify
//    it under the terms of the GNU General Public License as published by
//    the Free Software Foundation, either version 3 of the License, or
//    (at your option) any later version.
//
//    This lib is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//    GNU General Public License for more details.
//
//    You should have received a copy of the GNU General Public License
//    along with this lib.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Collections.Generic;
using System.Windows.Browser;

using System.IO;
using System.IO.IsolatedStorage;
using System.Json;
using System.Xml.Linq;
using System.Runtime.Serialization;
using System.Xml;

using ESRI.ArcGIS.Client;
using ESRI.ArcGIS.Client.Extensibility;
using ESRI.ArcGIS.Client.Actions;
using ESRI.ArcGIS.Client.Symbols;
using ESRI.ArcGIS.Client.Geometry;
using ESRI.ArcGIS.Client.Tasks;


namespace VUtils.ArcGIS.SLViewer {

	/// <summary>
	/// Layer derivatives helper, make some abstraction
	/// </summary>
    public class VLayer {

        public ESRI.ArcGIS.Client.Layer lyr = null;
        public string lyrName = "undefined name",
			lyrUrl = "undefined url",  // service url for layer or content for graphicslayer
            lyrType = "undefined type";

        public string ID { get { return lyr.ID; } }
        public bool Visible { get { return lyr.Visible; } }
        public string proxy = "";
        public bool selected = false;
		public bool popupOn = false; // on/off popups
		public string identifyLayerIds = ""; // sublayers id's with popups enabled

        public VLayer() {
            lyr = null;
        }

        public VLayer(Layer l) {
            lyr = l;
            getLyrSignature(null, l);
        }


        public VLayer(VLayerDescription ld) {
            lyr = null;
            lyrName = ld.name;
            lyrUrl = ld.url;
            lyrType = ld.type;
            selected = false;
            proxy = ld.proxy;

            helpCreateLayer(ld.id, true);
        } // public VLayer(VLayerDescription ld)


        public VLayer(JsonObject js) {
            lyr = null;
            lyrName = js["name"];
            lyrUrl = js["url"];
            lyrType = js["type"];
            proxy = getFromJson(js, "proxy");
			selected = getBoolFromJson(js, "selected");
			popupOn = getBoolFromJson(js, "popupEnabled");
			identifyLayerIds = getFromJson(js, "identifyLayerIds");

            helpCreateLayer(js["id"], js["visible"]);
        } // public VLayer(JsonObject js)


		public JsonObject toJson() {
			var obj = new JsonObject {
                    {"name", lyrName},
                    {"url", lyrUrl},
                    {"type", lyrType},
                    {"proxy", proxy},
                    {"id", ID},
                    {"visible", Visible},
                    {"selected", selected},
					{"popupEnabled", popupOn},
					{"identifyLayerIds", identifyLayerIds}
                };
			return obj;
		} // public JsonObject toJson()


        /// <summary>
        /// Create new Layer instance from this.lyr
        /// >>> foreach(var lyr in MapApplication.Current.Map.Layers) {
        /// >>>     var nl = new VLayer(lyr);
        /// >>>     nl.cloneLayer();
        /// >>>     frmPrint.Map.Layers.Add(nl.lyr);
        /// </summary>
        public void cloneLayer() {
            helpCreateLayer(lyr.ID, lyr.Visible);
        } // public void cloneLayer()


        private void helpCreateLayer(string id, bool vis) {
            lyr = createLayer(id, vis);
            if(lyr == null) {
                throw new Exception(string.Format(
                    "VLayer.helpCreateLayer, can't create layer [{0}, {1}]", id, lyrUrl));
            }
            lyr.Visible = vis;
            lyr.ID = id;
        } // private void helpCreateLayer(string id, bool vis)


        public JsonValue getFromJson(JsonObject js, string key) {
            // small helper
            if(js.ContainsKey(key)) return js[key];
            return "";
        } // public JsonValue getFromJson(JsonObject js, string key)

		public bool getBoolFromJson(JsonObject js, string key) {
			var jv = getFromJson(js, key);
			if(jv.ToString().Trim().ToLower().Equals("true")) return true;
			return false;
		} // public bool getBoolFromJson(JsonObject js, string key)


		/// <summary>
		/// Return FeatureLayer or null from ArcGISDynamicMapServiceLayer by lyrID
		/// </summary>
		/// <param name="lyrID">ArcGISDynamicMapServiceLayer sublayer id</param>
		/// <returns>VLayer with FeatureLayer inside</returns>
		public VLayer getSubLayer(int lyrID) {
			if(this.lyrType == "ArcGISDynamicMapServiceLayer") {
				var ld = new VUtils.ArcGIS.SLViewer.VLayerDescription();
				ld.type = "FeatureLayer";
				ld.url = string.Format("{0}/{1}", this.lyrUrl, lyrID);
				ld.proxy = this.proxy;
				var res = new VUtils.ArcGIS.SLViewer.VLayer(ld);
				return res;
			}
			else { return null; }
		} // private Layer getSubLayer(VUtils.ArcGIS.SLViewer.VLayer lyr, int lyrID)


		public string getFieldAlias(string fieldname) {
			if(this.lyrType != "FeatureLayer") {
				throw new Exception("VLayer.getFieldAlias, layer must be FeatureLayer");
			}
			var fl = this.lyr as FeatureLayer;
			if(fl.LayerInfo == null) {
				throw new Exception("VLayer.getFieldAlias, call lyr.Initialize() first");
			}

			var fields = fl.LayerInfo.Fields;
			foreach(var f in fields) {
				if(f.Name == fieldname) {
					return f.Alias;
				}
			}
			return "";
		} // public string getFieldAlias(string fieldname)


		/// <summary>
		/// create Layer according to its Type
		/// </summary>
		/// <param name="id"></param>
		/// <param name="vis"></param>
		/// <returns></returns>
        private Layer createLayer(string id, bool vis) {            
            string typ = lyrType;
            ESRI.ArcGIS.Client.Layer res = null;

            if(typ == "ArcGISTiledMapServiceLayer") {
                var lr = new ESRI.ArcGIS.Client.ArcGISTiledMapServiceLayer();
                lr.Url = lyrUrl;
                lr.ProxyURL = proxy;
                res = lr;
            }
            else if(typ == "OpenStreetMapLayer") {
                var lr = new ESRI.ArcGIS.Client.Toolkit.DataSources.OpenStreetMapLayer();
                res = lr;
            }
            else if(typ == "ArcGISDynamicMapServiceLayer") {
                var lr = new ESRI.ArcGIS.Client.ArcGISDynamicMapServiceLayer();
                lr.Url = lyrUrl;
                lr.ProxyURL = proxy;
                res = lr;
            }
            else if(typ == "FeatureLayer") {
                var lr = new ESRI.ArcGIS.Client.FeatureLayer();
                lr.Url = lyrUrl;
                lr.ProxyUrl = proxy;
                res = lr;
            }
            else if(typ == "GraphicsLayer") {
                var gl = setContent(id, lyrUrl);
                res = gl;
            }

			if(res != null) {
				ESRI.ArcGIS.Client.Extensibility.LayerProperties.SetIsPopupEnabled(res, popupOn);

				// sublayers popups on/off
				if(identifyLayerIds.Length <= 3) { ; }
				else {
					var xmlszn = new System.Xml.Serialization.XmlSerializer(typeof(System.Collections.ObjectModel.Collection<int>));
					var sr = new StringReader(identifyLayerIds);
					var ids = xmlszn.Deserialize(sr) as System.Collections.ObjectModel.Collection<int>;
					ESRI.ArcGIS.Mapping.Core.LayerExtensions.SetIdentifyLayerIds(res, ids);
				}
			}

            return res;
        } // private Layer createLayer(string id, bool vis)


        private void getLyrSignature(Map map, ESRI.ArcGIS.Client.Layer l) {
            // get all Layer parameters
            string typ = lyr.GetType().ToString();
            string[] parts = typ.Split(new string[] { "." }, StringSplitOptions.RemoveEmptyEntries);
            if(parts.Length > 0) typ = parts[parts.Length - 1];

            lyrType = typ;
            lyrName = MapApplication.GetLayerName(l);
			popupOn = ESRI.ArcGIS.Client.Extensibility.LayerProperties.GetIsPopupEnabled(l);

			// sublayers popups on/off http://forums.arcgis.com/threads/58106-Popup-for-visible-layers-only?highlight=popups
			var ids = ESRI.ArcGIS.Mapping.Core.LayerExtensions.GetIdentifyLayerIds(l);
			var xmlszn = new System.Xml.Serialization.XmlSerializer(typeof(System.Collections.ObjectModel.Collection<int>));
			var sw = new StringWriter();
			xmlszn.Serialize(sw, ids);
			identifyLayerIds = string.Format("{0}", sw.ToString().Trim());

            if(typ == "ArcGISTiledMapServiceLayer") {
                var lr = (ArcGISTiledMapServiceLayer)lyr;
                lyrUrl = lr.Url;
                proxy = lr.ProxyURL;
            }
            else if(typ == "OpenStreetMapLayer") {
                var lr = lyr as ESRI.ArcGIS.Client.Toolkit.DataSources.OpenStreetMapLayer;
                lyrUrl = "http://www.openstreetmap.org/";
            }
            else if(typ == "ArcGISDynamicMapServiceLayer") {
                var lr = (ArcGISDynamicMapServiceLayer)lyr;
                lyrUrl = lr.Url;
                proxy = lr.ProxyURL;
            }
            else if(typ == "FeatureLayer") {
                var lr = (FeatureLayer)lyr;
                lyrUrl = lr.Url;
                proxy = lr.ProxyUrl;
            }
            else if(typ == "GraphicsLayer") {
                var lr = (GraphicsLayer)lyr;
                lyrUrl = getContent(lr);
                proxy = "";
            }
            return;
        } // private string getLyrSignature(Map map, ESRI.ArcGIS.Client.Layer lyr)


        public static string getContent(GraphicsLayer gl) {
            // serialize GraphicsLayer
            var sb = new System.Text.StringBuilder();
            var xw = XmlWriter.Create(sb);
            gl.SerializeGraphics(xw);
            xw.Close();
            return sb.ToString();
        } // public string getContent()


        private ESRI.ArcGIS.Client.GraphicsLayer setContent(string id, string xmlContent) {
            // create and deserialize GraphicsLayer
            var gl = new ESRI.ArcGIS.Client.GraphicsLayer() {
                ID = id,
                Renderer = new SimpleRenderer() {
                    Symbol = new SimpleMarkerSymbol()
                }
            };
            gl.RendererTakesPrecedence = false;
            // Set layer name in Map Contents
            gl.SetValue(MapApplication.LayerNameProperty, lyrName);

            gl = setContent(gl, xmlContent);
            return gl;
        } // private ESRI.ArcGIS.Client.GraphicsLayer setContent(string id, string xmlContent)


        public static ESRI.ArcGIS.Client.GraphicsLayer setContent(
            ESRI.ArcGIS.Client.GraphicsLayer gl, string xmlContent)
        {
            var sr = new StringReader(xmlContent);
            var xr = XmlReader.Create(sr);
            //gl.Graphics.Clear();
            gl.DeserializeGraphics(xr);
            xr.Close();
            sr.Close();
            return gl;
        } // public static ESRI.ArcGIS.Client.GraphicsLayer setContent(ESRI.ArcGIS.Client.GraphicsLayer gl, string xmlContent)


        public string getAGSMapServiceUrl() {
            var res = "";
            if(lyrType == "ArcGISDynamicMapServiceLayer" ||
                lyrType == "ArcGISTiledMapServiceLayer") {
                res = lyrUrl;
            }
            else {
                if(lyrType == "FeatureLayer") {
                    var pos = lyrUrl.LastIndexOf("/");
                    res = lyrUrl.Substring(0, pos);
                }
            }
            return res;
        } // public string getAGSMapServiceUrl()

    } // class VLayer


    /// <summary>
    /// Layer parameters from layersRepository
    /// </summary>
    public class VLayerDescription: Object {
        public string id, name, type, topic, url, proxy;

        private string _preview = "";
        public string preview {
            get {
                if(_preview == "") { return "preview/default.png"; }
                return _preview;
            }
            set { _preview = value; }
        }

        public string printedName { get { return ToString(); } }

        override public string ToString() {
            return String.Format("{1} ({0})", id, name);
        }
        public string toString() {
            return String.Format("id [{0}], name [{1}], type [{2}], topic [{3}], url [{4}], proxy [{5}], preview [{6}]",
                id, name, type, topic, url, proxy, preview);
        }
        public string getFromXml(XAttribute attr) {
            if(attr == null) return "";
            return attr.Value.Trim();
        }
    } // public class VLayerDescription: Object


    /// <summary>
    /// log to browser console (ie8 script console or FF firebug)
    /// </summary>
    public static class VExtClass { // http://kodierer.blogspot.com/2009/05/silverlight-logging-extension-method.html
        /// <summary>
        /// if you are using Firefox with the Firebug add-on or
        /// Internet Explorer 8: Use the console.log mechanism
        /// </summary>
        /// <param name="obj"></param>
        public static void clog(this object obj) {
            try {
                HtmlWindow window = HtmlPage.Window;
                var isConsoleAvailable = (bool)window.Eval(
                    "typeof(console) != 'undefined' && typeof(console.log) != 'undefined'");
                if(isConsoleAvailable == false) return;

                var console = (window.Eval("console.log") as ScriptObject);
				//var console = (window.Eval("slLog") as ScriptObject);

                //DateTime dt = DateTime.Now;
                //var txt = string.Format("{0} {1}\n", dt.ToString("yyyy-MM-dd hh:mm:ss"), obj);
                var txt = string.Format("{0}\n", obj);
                console.InvokeSelf(txt);
            }
            catch(Exception ex) {
                var msg = ex.Message;
                //MessageBox.Show(msg);
            }
        } // public static void clog(this object obj)


    } // public static class VExtClass


    public delegate void logFunc(string msg); // http://msdn.microsoft.com/en-us/library/ms173172%28v=VS.80%29.aspx


///////////////////////////////////////////////////////////////////////
// serialize GraphicLayer http://forums.arcgis.com/threads/8774-save-layer-to-xml-file
///////////////////////////////////////////////////////////////////////
	/// <summary>
	/// Class for serializing a GraphicCollection.
	/// </summary>
	[CollectionDataContract(Name = "Graphics", ItemName = "Graphic")]
	public class SerializableGraphicCollection : List<SerializableGraphic>
	{
		public SerializableGraphicCollection() { }

		public SerializableGraphicCollection(GraphicCollection graphicCollection)
		{
			foreach (Graphic g in graphicCollection)
			{
				Add(new SerializableGraphic(g));
			}
		}
	} // public class SerializableGraphicCollection : List<SerializableGraphic>


	/// <summary>
	/// Class for serializing a Graphic.
	/// </summary>
	[DataContract]
	public class SerializableGraphic
	{
		public SerializableGraphic() { }

		public SerializableGraphic(Graphic graphic)
		{
			Geometry = graphic.Geometry;
			Attributes = new SerializableAttributes(graphic.Attributes);
		}

		[DataMember]
		public SerializableAttributes Attributes;

		[DataMember]
		public ESRI.ArcGIS.Client.Geometry.Geometry Geometry;

		/// <summary>
		/// Gets the underlying graphic (useful after deserialization).
		/// </summary>
		/// <value>The graphic.</value>
		internal Graphic Graphic
		{
			get
			{
				Graphic g = new Graphic() { Geometry = Geometry };
				foreach (KeyValuePair<string, object> kvp in Attributes)
				{
					g.Attributes.Add(kvp);
				}
				return g;
			}
		}
	} // public class SerializableGraphic


	/// <summary>
	/// Class for serialization of Attributes.
	/// </summary>
	[CollectionDataContract(ItemName = "Attribute")]
	public class SerializableAttributes : List<KeyValuePair<string, object>>
	{
		public SerializableAttributes() { }

		public SerializableAttributes(IEnumerable<KeyValuePair<string, object>> items)
		{
			foreach (KeyValuePair<string, object> item in items)
				Add(item);
		}
	} // public class SerializableAttributes : List<KeyValuePair<string, object>>


	/// <summary>
	/// GraphicsLayer extension class to serialize/deserialize to XML the graphics of a graphics/feature layer
	/// Note : the symbols of the graphics are not serialized (==> working well if there is a renderer but not working without renderer (except if the symbol is initialized by code after deserialization))
	/// </summary>
	public static class GraphicsLayerExtension
	{
		public static void SerializeGraphics(this GraphicsLayer graphicsLayer, XmlWriter writer)
		{
			XMLSerialize(writer, new SerializableGraphicCollection(graphicsLayer.Graphics));
		}


		public static void DeserializeGraphics(this GraphicsLayer graphicsLayer, XmlReader reader)
		{
			foreach (SerializableGraphic g in XMLDeserialize<SerializableGraphicCollection>(reader))
			{
				graphicsLayer.Graphics.Add(g.Graphic);
			}
		}


		private static void XMLSerialize<T>(XmlWriter writer, T data)
		{
			var serializer = new DataContractSerializer(typeof(T));
			serializer.WriteStartObject(writer, data);

			// Optimizing Away Repeat XML Namespace Declarations
			writer.WriteAttributeString("xmlns", "sys", null, "http://www.w3.org/2001/XMLSchema");
			writer.WriteAttributeString("xmlns", "esri", null, "http://schemas.datacontract.org/2004/07/ESRI.ArcGIS.Client.Geometry");
			writer.WriteAttributeString("xmlns", "col", null, "http://schemas.datacontract.org/2004/07/System.Collections.Generic");

			serializer.WriteObjectContent(writer, data);
			serializer.WriteEndObject(writer);
		}


		private static T XMLDeserialize<T>(XmlReader reader)
		{
			var serializer = new DataContractSerializer(typeof(T));
			T data = (T)serializer.ReadObject(reader);
			return data;
		}
	} // public static class GraphicsLayerExtension
///////////////////////////////////////////////////////////////////////
// serialize GraphicLayer
///////////////////////////////////////////////////////////////////////


	public class VRelationInfo {
		//relationsListForm.listBox1.Items.Add(string.Format("linkID: {0}, linkName: {1}, tableID: {2}", 
		// r.Id, r.Name, r.RelatedTableId));
		//var rels = relatesLayer.LayerInfo.Relationships;
		//var r = rels.First();
		public string name, descr;
		public int id, tableId;
		public ESRI.ArcGIS.Client.FeatureService.Relationship relObj;

		public VRelationInfo(ESRI.ArcGIS.Client.FeatureService.Relationship rel) {
			relObj = rel;
			id = rel.Id;
			name = rel.Name;
			tableId = rel.RelatedTableId;
			descr = string.Format("linkID: {0}, linkName: {1}, tableID: {2}", id, name, tableId);
		} // public VRelationInfo(ESRI.ArcGIS.Client.FeatureService.Relationship rel)


		public VRelationInfo(string description) {
			descr = description;
			relObj = null;
			id = -1;
			name = "";
			tableId = -1;

			string[] parts = descr.Split(new string[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
			if(parts.Length != 3) throw new Exception("VRelationInfo, malformed relation description" + ": " + descr);
			foreach(var part in parts) {
				var items = part.Split(new string[] { ": " }, StringSplitOptions.RemoveEmptyEntries);
				if(items.Length != 2) throw new Exception("VRelationInfo, malformed relation description" + ": " + descr);

				if(items[0] == "linkID") {
					id = Int32.Parse(items[1]);
				}
				else if(items[0] == "linkName") {
					name = items[1];
				}
				else if(items[0] == "tableID") {
					tableId = Int32.Parse(items[1]);
				}
				else {
					throw new Exception("VRelationInfo, malformed relation description" + ": " + descr);
				}
			}
		} // public VRelationInfo(string description)

	} // public class VRelationInfo

} // namespace VUtils.ArcGIS.SLViewer
