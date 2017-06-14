using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Caramelia.Data.Web {
    public class DownloadOrder {
        public bool IsDownloadStockPrice { get; set; }
        public bool IsDownloadXbrl { get; set; }

    }
    public class DownloadManager {
        string _local;
        
        public string LocalPath {
            get { return _local; }
            set { _local = value; }
        }
        public void Download(DownloadManager dm) {

        }
        public StockPriceDLM StockPriceDlm { get; }
        public XbrlDLM XbrlDlm { get; }

    }
    public class StockPriceDLM { }
    public class XbrlDLM { }
    public class StockSplitDLM { }
}
