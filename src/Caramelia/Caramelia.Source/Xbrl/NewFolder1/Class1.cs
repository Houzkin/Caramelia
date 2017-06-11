using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.IO;
using Caramelia;

namespace Calamelia.Xbrl {
	public enum AtomTarget {
		EDINET,TDnet,
	}
	public class XmlCollector {
		System.Net.WebClient wc = new System.Net.WebClient() { Encoding = Encoding.GetEncoding("utf-8") };
		public virtual XDocument Acqire(string url) {
			return XDocument.Parse(wc.DownloadString(url));
		}
	}
	public class XmlProvider : XmlCollector {
		public bool IsDownloaded(string url) {
			//var path = savePath(url);
			//return File.Exists(path);
			throw new NotImplementedException();
		}
		public void DownloadAndSave(string url,string ticker) {
			//var path = savePath(url);
			//this.Acqire(url).Save(path);
			throw new NotImplementedException();
		}
		public void DownloadAndSave(string ticker, string docid, string[] url,XDocument entry ) {
			var us = url.Select(a => new { local = toSaveUri(a, ticker, docid), src = a });
			var s = us.Where(a => !string.IsNullOrEmpty(a.local.Item1));
			var u = s.First().local.Item1;
			if (s.All(a => a.local.Item1 != u)) throw new InvalidOperationException();
			foreach(var doc in us) {
				this.Acqire(doc.src).Save(doc.local.Item1 + doc.local.Item2);
			}
			entry.Save(u + "EntryInfo.xml", SaveOptions.OmitDuplicateNamespaces);

		}
		Tuple<string,string> toSaveUri(string url,string ticker,string docid) {
			Uri uri =new Uri(url); // null;
			//try {
			//	uri = 
			//}catch (UriFormatException) {
			//	return Tuple.Create("", url);
			//}
			//var uri = new Uri(url);
			if (uri.IsLoopback)// return Tuple.Create(Path.GetDirectoryName(url)+Path.DirectorySeparatorChar, Path.GetFileName(url));
				throw new InvalidOperationException();

			var path = Local.CurrentPath+"xbrl"+Path.DirectorySeparatorChar;

			var dis = uri.LocalPath.Split('/');
			string tgt = "";
			if (dis.Any(a => a == "taxonomy")) {
				tgt = dis.SkipWhile(a => a != "taxonomy").Aggregate((a, b) => a + Path.DirectorySeparatorChar + b);
			} else if(dis.Any(a=>a == "edinet")) {
				path = path + ticker + Path.DirectorySeparatorChar + docid + Path.DirectorySeparatorChar;
				tgt = "edinet" + dis.Last();
			}else if(dis.Any(a=>a == "tdnet")) {
				path = path + ticker + Path.DirectorySeparatorChar + docid + Path.DirectorySeparatorChar;
				tgt = "tdnet" + dis.Last();
			} else {
				tgt = dis.Aggregate((a, b) => a + Path.DirectorySeparatorChar + b);
			}
			return Tuple.Create(path, tgt);//path + tgt;
		}
		string toSaveName(string url,string ticker,string docid) {
			return toSaveUri(url,ticker,docid).Item2;
		}
		string toSavePath(string url,string ticker,string docid) {
			return toSaveUri(url,ticker,docid).Item1;
		}
		string toSaveFullName(string url,string ticker,string docid) {
			var u = toSaveUri(url,ticker,docid);
			return u.Item1 + u.Item2;
		}
	
	}
	public class AtomDownloader {
		XmlProvider xp;
		AtomTarget Source { get; set; }
		public AtomDownloader(AtomTarget src) {
			Source = src;
			xp = new XmlProvider();
		}
		public void Update() { }
		public void Update(int ticker) { }

		private void update(string url) {
			var atm = xp.Acqire(url);
			var ns = atm.Root.Name.Namespace;

			do {
				var ent = atm.Descendants(ns + "entry")
					.Select(a => _next(a, ns));

				var nxt = atm.Descendants(ns + "link")
					.Where(a => a.Attribute("rel").Value == "next")
					.Select(a => a.Attribute("href").Value)
					.FirstOrDefault();

				if (!ent.Any() || ent.Last() == false || nxt == null) atm = null;
				else atm = xp.Acqire(nxt);

			} while (atm != null);
		}
		private bool _next(XElement entry, XNamespace ns) {
			var urls = entry.Elements(ns + "link")
				.Where(a => a.Attribute("rel").Value == "related")
				.Where(a => a.Attribute("type").Value == "text/xml")
				.Select(a => a.Attribute("href").Value)
				.Where(a => !xp.IsDownloaded(a));
			//regix ダウンロードファイルの選別
			if (!urls.Any()) return false;

			var title = entry.Element(ns + "title");
			//var docid = entry.Element(ns + "docid");
			var id = entry.Element(ns + "id");
			var updated = entry.Element(ns + "updated");
			var ticker = entry.Parent.Element(ns + "id").Value.Split('/').Last();

			foreach(var u in urls) 
				xp.DownloadAndSave(u,ticker);

			return true;
		}
	}
}
