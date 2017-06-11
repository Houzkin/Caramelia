using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.IO;
using System.Text.RegularExpressions;
using Calamelia.Xbrl;
using Houzkin;
using Caramelia;
using JeffFerguson.Gepsio;

namespace Calamelia.XmlPrivide {

	enum resType { taxonomy,tdnet,edinet,}
	//public class XmlCollector {
	//	System.Net.WebClient wc = new System.Net.WebClient() { Encoding = Encoding.GetEncoding("utf-8") };
	//	public virtual XDocument Acqire(Uri url) {
	//		return XDocument.Parse(wc.DownloadString(url));
	//	}
	//}
	public class XmlProvider {
		static string getCurrentPath() { return Local.CurrentPath + "XbrlFiles" + Path.DirectorySeparatorChar; }
		static string toLocalPath(Uri url) {
			//var ur = new Uri(url);
			var hst = url.DnsSafeHost;
			var uri = getCurrentPath() + hst + url.LocalPath.Replace('/', Path.DirectorySeparatorChar);
			return uri;
		}
		static Uri toUrl(string path) {
			path = path.Replace(getCurrentPath(), "");
			var dis = path.Split(Path.DirectorySeparatorChar);
			var hst = "http://" + dis.First();
			var u = dis.Skip(1).Aggregate(hst,(a, b) => a + '/' + b);
			return new Uri(u);
		}
		public bool IsDownloaded(Uri url) {
			var u = toLocalPath(url);
			return File.Exists(u);
		}
		public XDocument Acqire(Uri url) {
			DownloadAndSave(url);
			return XDocument.Load(toLocalPath(url));
		}
		public void DownloadAndSave(Uri url) {
			if (IsDownloaded(url)) return;
			//var doc = base.Acqire(url);
			var doc = acqire(url);
			doc.Save(toLocalPath(url));
		}
		private XDocument acqire(Uri url) {
			System.Net.WebClient wc = new System.Net.WebClient() { Encoding = Encoding.GetEncoding("utf-8") };
			return XDocument.Parse(wc.DownloadString(url));
		}
		public void DownloadAndSave(Uri[] urls) {
			foreach (var url in urls) DownloadAndSave(url);
		}
	}
	/// <summary>URLが示すデータのインスタンスを提供する。</summary>
	public class InstanceProvider {
		static Dictionary<Uri, WeakReference<XbrlDocument>> wdic = new Dictionary<Uri, WeakReference<XbrlDocument>>();
		XmlProvider xp = new XmlProvider();
		Uri current { get; }
		public InstanceProvider(Uri url) { current = url; }
		
		public XbrlDocument Acqire() {
			return acqire(current);
		}
		public XbrlDocument Acqire(string relative) {
			var nu = new Uri(current, relative);
			return acqire(nu);
		}
		public XbrlDocument Acqire(Uri uor) {
			if (uor.IsAbsoluteUri) return acqire(uor);
			return Acqire(uor.OriginalString);
		}
		XbrlDocument acqire(Uri url) {
			var r = ResultWithValue.Of<Uri, WeakReference<XbrlDocument>>(wdic.TryGetValue, url)
				.TrueOrNot(
					o => ResultWithValue.Of<XbrlDocument>(o.TryGetTarget).Value,
					x => null);
			if (r != null) return r;
			deflag();
            //var ins = XbrlParser.ParseXmlFile(xp.Acqire(url));
            var ins = new XbrlDocument();
            ins.Load(url.ToString());
			wdic.Add(url, new WeakReference<XbrlDocument>(ins));
			return ins;
		}
		static void deflag() {
			var nonks = new List<Uri>();
			foreach(var kvp in wdic) 
				if (!ResultWithValue.Of<XbrlDocument>(kvp.Value.TryGetTarget)) 
					nonks.Add(kvp.Key);
			foreach (var k in nonks) wdic.Remove(k);
		}
	}
	//public class TaxonomyManager : XmlProvider {
	//	static TaxonomyManager 〆manager;
	//	public static TaxonomyManager Singleton {
	//		get {
	//			if (〆manager == null) 〆manager = new TaxonomyManager();
	//			return 〆manager;
	//		}
	//	}
	//	public new XbrlElement Acqire(Uri url) {
	//		throw new NotImplementedException();
	//	}
	//}
}
