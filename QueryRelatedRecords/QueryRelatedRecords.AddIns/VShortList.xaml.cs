using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace QueryRelatedRecords.AddIns {

    public partial class VShortList: UserControl {

        private QueryRelatedTool app = null;

        public VShortList(QueryRelatedTool p) {
            app = p;
            InitializeComponent();
        }

        private void button1_Click(object sender, RoutedEventArgs e) {
            //this.Visibility = System.Windows.Visibility.Collapsed;
            app.log("VShortList, user hit 'OK'");
            ESRI.ArcGIS.Client.Extensibility.MapApplication.Current.HideWindow(app.relationsListForm);
        }
    }
}
