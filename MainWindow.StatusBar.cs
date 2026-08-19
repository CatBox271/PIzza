using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiWpfUi
{
    public partial class MainWindow
    {
        public void State(string text = "就绪")
        {
            _ = Dispatcher.BeginInvoke(() => { StatusBar.Text = text; });
        }
    }
}
