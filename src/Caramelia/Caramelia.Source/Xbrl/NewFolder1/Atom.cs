using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Calamelia.XmlPrivide {
	public enum AtomTarget {
		EDINET, TDnet,
	}

	public class AtomDownloader {
		XmlProvider xp;
		public AtomDownloader() {
			xp = new XmlProvider();
		}
		public void Update(AtomTarget tgt) {
			if (tgt == AtomTarget.EDINET)
				update("http://resource.ufocatch.com/atom/edinetx");
			else if (tgt == AtomTarget.TDnet)
				update("http://resource.ufocatch.com/atom/TDnetx");
		}
		public void Update(AtomTarget tgt,string ticker) {
			if (tgt == AtomTarget.EDINET)
				update("http://resource.ufocatch.com/atom/edinetx/query/" + ticker);
			else if (tgt == AtomTarget.TDnet)
				update("http://resource.ufocatch.com/atom/TDnetx/query/" + ticker);
		}

		private void update(string url) {
			var atm = xp.Acqire(new Uri(url));
			var ns = atm.Root.Name.Namespace;

			do {
				var ent = atm.Descendants(ns + "entry")
					.Select(a => downloadEntry(a, ns));

				var nxt = atm.Descendants(ns + "link")
					.Where(a=>a.Attribute("rel") != null)
					.Where(a => a.Attribute("rel").Value == "next")
					.Select(a => a.Attribute("href").Value)
					.FirstOrDefault();

				if (!ent.Any() || ent.Last() == false || nxt == null) atm = null;
				else atm = xp.Acqire(new Uri(nxt));

			} while (atm != null);
		}
		private bool downloadEntry(XElement entry, XNamespace ns) {

			var title = entry.Element(ns + "title");
			var id = entry.Element(ns + "id");
			var updated = entry.Element(ns + "updated");

			var code = Regex.Match(title.Value, @"【(?<code>[A-Z0-9]*)】").Groups["code"].Value;

			var urls = entry.Elements(ns + "link")
				.Where(a => a.Attribute("rel").Value == "related")
				.Where(a => a.Attribute("type").Value == "text/xml")
				.Select(a => a.Attribute("href").Value)
				.Where(a => !xp.IsDownloaded(new Uri(a)));

			var r = new Regex(@"(edinet/.*?PublicDoc|tdnet/.*?Attachment)/(?<name>.+)");
			urls = urls.Where(a => r.IsMatch(a));

			//regix ダウンロードファイルの選別
			if (!urls.Any()) return false;

			xp.DownloadAndSave(urls.Select(a=>new Uri(a)).ToArray());

			return true;
		}
		
	}
}
