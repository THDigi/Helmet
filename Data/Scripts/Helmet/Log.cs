using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.ModAPI;
using Sandbox.Common;

namespace Digi.Utils
{
    class Log
    {
        private const string MOD_NAME = "Helmet";
        private const string LOG_FILE = "info.log";
        
        private static System.IO.TextWriter writer;
        
        public static void Error(Exception e)
        {
            Error(e.ToString());
        }
        
        public static void Error(string msg)
        {
            Info("ERROR: " + msg);
            
            try
            {
                MyAPIGateway.Utilities.ShowNotification(MOD_NAME + " error - open %AppData%/SpaceEngineers/Storage/..._"+MOD_NAME+"/"+LOG_FILE+" for details", 10000, MyFontEnum.Red);
            }
            catch(Exception e)
            {
                Info("ERROR: Could not send notification to local client: " + e.ToString());
            }
        }
        
        public static void Info(string msg)
        {
            Write(msg);
        }
        
        private static void Write(string msg)
        {
            if(writer == null)
            {
                if(MyAPIGateway.Utilities == null)
                    throw new Exception("API not initialied but got a log message: " + msg);
                
                writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(LOG_FILE, typeof(Log));
            }
            
            writer.WriteLine(DateTime.Now.ToString("[HH:mm:ss] ") + msg);
            writer.Flush();
        }
        
        public static void Close()
        {
            if(writer != null)
            {
                writer.Flush();
                writer.Close();
            }
        }
    }
}
