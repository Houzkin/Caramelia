using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Caramelia {
    public static class Local {
        static string rootPath = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;
        public static string CurrentPath {
            get { return rootPath; }
            set { rootPath = value; }
        }
    }
}
