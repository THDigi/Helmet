using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Engine;
using Sandbox.Engine.Physics;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Common.Utils;
using VRageMath;
using VRage;
using Digi.Utils;

namespace Digi.Helmet
{
    public class Icons
    {
        public const int WARNING = 0;
        public const int HEALTH = 1;
        public const int ENERGY = 2;
        public const int OXYGEN = 3;
        public const int INVENTORY = 4;
        public const int BROADCASTING = 5;
        public const int DAMPENERS = 6;
        public const int OXYGEN_ENV = 7;
        public const int GRAVITY = 8;
    };
    
    public class Settings
    {
        private const string FILE = "helmet.cfg";
        
        public bool enabled = true;
        public string helmetModel = "vignette";
        public bool hud = true;
        public bool autoFovScale = false;
        public double scale = 0.0f;
        public double hudScale = 0.0f;
        public bool reminder = true;
        public float warnBlinkTime = 0.25f;
        public float delayedRotation = 0.5f;
        
        //public string[] helmetModels = new string[] { "off", "vignette", "helmetmesh" };
        
        public const int TOTAL_ICONS = 9;
        public string[] iconNames = new string[TOTAL_ICONS] { "warning", "health", "energy", "oxygen", "inventory", "broadcasting", "dampeners", "oxygenenv", "gravity"};
        public bool[] iconBar = new bool[TOTAL_ICONS] { false, true, true, true, true, false, false, false, false };
        public bool[] iconShow = new bool[TOTAL_ICONS] { true, true, true, true, true, true, true, true, true };
        public double[] iconLeft = new double[TOTAL_ICONS] { 0, 0.085, -0.075, 0.075, -0.085, 0.082, 0.074, 0.075, 0.0 };
        public double[] iconUp = new double[TOTAL_ICONS] { 0.035, -0.062, -0.07, -0.07, -0.062, -0.077, -0.076, -0.07, -0.06 };
        public bool[] iconFlipVertical = new bool[TOTAL_ICONS] { false, false, true, false, true, false, false, false, false };
        public int[] iconWarnPercent = new int[TOTAL_ICONS] { -1, 15, 15, 15, -1, -1, -1, -1, -1 };
        public bool[] iconNoHud = new bool[TOTAL_ICONS] { false, false, true, false, false, false, false, false, false };
        
        public const double MIN_SCALE = -1.0f;
        public const double MAX_SCALE = 1.0f;
        
        public const double MIN_HUDSCALE = -1.0f;
        public const double MAX_HUDSCALE = 1.0f;
        
        public const float MIN_DELAYED = 0.0f;
        public const float MAX_DELAYED = 1.0f;
        
        private static char[] CHARS = new char[] { '=' };
        
        public Settings()
        {
            if(!Load())
            {
                ScaleForFOV(MathHelper.ToDegrees(MyAPIGateway.Session.Config.FieldOfView));
            }
            
            Save();
        }
        
        public bool Load()
        {
            try
            {
                if(MyAPIGateway.Utilities.FileExistsInLocalStorage(FILE, typeof(Settings)))
                {
                    var file = MyAPIGateway.Utilities.ReadFileInLocalStorage(FILE, typeof(Settings));
                    ReadSettings(file);
                    file.Close();
                    return true;
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
            
            return false;
        }
        
        private void ReadSettings(TextReader file)
        {
            try
            {
                string line;
                string[] args;
                int i;
                bool b;
                float f;
                double d;
                bool lookForIndentation = false;
                int currentId = -1;
                
                while((line = file.ReadLine()) != null)
                {
                    if(line.Length == 0)
                        continue;
                    
                    i = line.IndexOf("//");
                    
                    if(i > -1)
                        line = (i == 0 ? "" : line.Substring(0, i));
                    
                    if(line.Length == 0)
                        continue;
                    
                    args = line.Split(CHARS, 2);
                    
                    if(args.Length != 2)
                    {
                        Log.Error("Unknown config.cfg line: "+line+"\nMaybe is missing the '=' ?");
                        continue;
                    }
                    
                    if(lookForIndentation && args[0].StartsWith("  "))
                    {
                        args[0] = args[0].Trim().ToLower();
                        args[1] = args[1].Trim().ToLower();
                        
                        switch(args[0])
                        {
                            case "up":
                                if(double.TryParse(args[1], out d))
                                    iconUp[currentId] = d;
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                            case "left":
                                if(double.TryParse(args[1], out d))
                                    iconLeft[currentId] = d;
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                            case "flipvertical":
                                if(bool.TryParse(args[1], out b))
                                    iconFlipVertical[currentId] = b;
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                            case "warnpercent":
                                if(int.TryParse(args[1], out i))
                                    iconWarnPercent[currentId] = i;
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                            case "nohud":
                                if(bool.TryParse(args[1], out b))
                                    iconNoHud[currentId] = b;
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                        }
                    }
                    else
                    {
                        lookForIndentation = false;
                        args[0] = args[0].Trim().ToLower();
                        args[1] = args[1].Trim().ToLower();
                        
                        switch(args[0])
                        {
                            case "enabled":
                                if(bool.TryParse(args[1], out b))
                                    enabled = b;
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                                /*
                            case "helmetmodel":
                                string valid = null;
                                foreach(var name in helmetModels)
                                {
                                    if(name == args[1])
                                    {
                                        valid = name;
                                        break;
                                    }
                                }
                                if(valid != null)
                                    helmetModel = (valid == "off" ? null : valid);
                                else
                                    Log.Error("Inexistent "+args[0]+" value: " + args[1]);
                                continue;
                                 */
                            case "hud":
                                if(bool.TryParse(args[1], out b))
                                    hud = b;
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                            case "autofovscale":
                                if(bool.TryParse(args[1], out b))
                                    autoFovScale = b;
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                            case "scale":
                                if(double.TryParse(args[1], out d))
                                    scale = Math.Min(Math.Max(d, MIN_SCALE), MAX_SCALE);
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                            case "hudscale":
                                if(double.TryParse(args[1], out d))
                                    hudScale = Math.Min(Math.Max(d, MIN_HUDSCALE), MAX_HUDSCALE);
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                            case "warnblinktime":
                                if(float.TryParse(args[1], out f))
                                    warnBlinkTime = f;
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                            case "reminder":
                                if(bool.TryParse(args[1], out b))
                                    reminder = b;
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                            case "delayedrotation":
                                if(float.TryParse(args[1], out f))
                                    delayedRotation = Math.Min(Math.Max(f, MIN_DELAYED), MAX_DELAYED);
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                        }
                        
                        for(int id = 0; id < TOTAL_ICONS; id++)
                        {
                            if(args[0] == iconNames[id])
                            {
                                currentId = id;
                                lookForIndentation = true;
                                
                                if(bool.TryParse(args[1], out b))
                                    iconShow[currentId] = b;
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                
                                break;
                            }
                        }
                        
                        if(lookForIndentation)
                            continue;
                    }
                    
                    Log.Error("Unknown setting: " + args[0]);
                }
                
                Log.Info("Loaded settings:\n" + GetSettingsString(false));
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        public void ScaleForFOV(float fov)
        {
            double fovScale = ((Math.Min(Math.Max(fov, 40), 90) - 60) / 30.0);
            
            if(fovScale > 0)
                fovScale *= 0.65;
            else if(fovScale < 0)
                fovScale *= 1.2;
            
            fovScale = Math.Round(fovScale, 2);
            
            this.scale = Math.Min(Math.Max(fovScale, MIN_SCALE), MAX_SCALE);
            this.hudScale = Math.Min(Math.Max(fovScale, MIN_HUDSCALE), MAX_HUDSCALE);
        }
        
        public void Save()
        {
            try
            {
                var file = MyAPIGateway.Utilities.WriteFileInLocalStorage(FILE, typeof(Settings));
                file.Write(GetSettingsString(true));
                file.Flush();
                file.Close();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        public string GetSettingsString(bool comments)
        {
            var str = new StringBuilder();
            
            if(comments)
            {
                str.AppendLine("// Helmet mod config; this file gets automatically overwritten!");
                str.AppendLine("// Lines starting with // are comments");
                str.AppendLine();
            }
            
            str.AppendLine("enabled="+enabled+(comments ? " // enable the mod ?" : ""));
            //str.AppendLine("helmetmodel="+helmetModel+(comments ? " // options: "+String.Join(", ", helmetModels) : ""));
            str.AppendLine("hud="+hud+(comments ? " // enable the HUD ?" : ""));
            str.AppendLine("autofovscale="+autoFovScale+(comments ? " // if true it automatically sets 'scale' and 'hudscale' when changing FOV" : ""));
            str.AppendLine("scale="+scale+(comments ? " // the helmet glass scale, -1.0 to 1.0, default 0 for FOV 60" : ""));
            str.AppendLine("hudscale="+hudScale+(comments ? " // the entire HUD scale, -1.0 to 1.0, default 0 for FOV 60" : ""));
            str.AppendLine("warnblinktime="+warnBlinkTime+(comments ? " // the time between each hide/show of the warning icon and its respective bar" : ""));
            str.AppendLine("delayedrotation="+delayedRotation+(comments ? " // 0.0 to 1.0, how much to delay the helmet when rotating view" : ""));
            
            if(comments)
            {
                str.AppendLine();
                str.AppendLine("// Individual icon configuration (advanced)");
                str.AppendLine("// Use /helmet reload in-game to reload the config after you've edited it.");
            }
            
            for(int id = 0; id < TOTAL_ICONS; id++)
            {
                str.AppendLine(iconNames[id]+"="+iconShow[id]+(comments ? " // display this icon or not" : ""));
                str.AppendLine("  up="+iconUp[id]+(comments ? " // position from the center towards up, use negative values for down" : ""));
                str.AppendLine("  left="+iconLeft[id]+(comments ? " // position from the center towards left, use negative values for right" : ""));
                str.AppendLine("  nohud="+iconNoHud[id]+(comments ? " // if true this icon will only appear when the game HUD is hidden" : ""));
                
                if(iconWarnPercent.Length > id && iconWarnPercent[id] > -1)
                    str.AppendLine("  warnpercent="+iconWarnPercent[id]+(comments ? " // warning % for this statistic" : ""));
            }
            
            if(comments)
                str.AppendLine();
            
            str.AppendLine("reminder="+reminder+(comments ? " // do not change, this set to true will nag you to type /helmet until you do." : ""));
            
            return str.ToString();
        }
        
        public void Close()
        {
        }
    }
}