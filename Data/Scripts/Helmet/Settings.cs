using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Sandbox.Common;
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
using VRageRender;

namespace Digi.Helmet
{
    public class HudElement
    {
        public string name;
        public int show = 1;
        public bool hasBar = false;
        public double posLeft = 0;
        public double posUp = 0;
        public bool flipHorizontal = false;
        public int warnPercent = -1;
        public int warnMoveMode = 0;
        public int hudMode = 0;
        
        public HudElement(string name)
        {
            this.name = name;
        }
        
        public HudElement Copy()
        {
            return new HudElement(name)
            {
                show = this.show,
                hasBar = this.hasBar,
                posLeft = this.posLeft,
                posUp = this.posUp,
                flipHorizontal = this.flipHorizontal,
                warnPercent = this.warnPercent,
                warnMoveMode = this.warnMoveMode,
                hudMode = this.hudMode,
            };
        }
    }
    
    public enum SpeedUnits
    {
        mps,
        kph,
    }
    
    public class Icons
    {
        public const int WARNING = 0;
        public const int HEALTH = 1;
        public const int ENERGY = 2;
        public const int OXYGEN = 3;
        public const int OXYGEN_ENV = 4;
        public const int HYDROGEN = 5;
        public const int INVENTORY = 6;
        public const int BROADCASTING = 7;
        public const int DAMPENERS = 8;
        public const int VECTOR = 9;
        public const int DISPLAY = 10;
        public const int HORIZON = 11;
    };
    
    public class Settings
    {
        private const string FILE = "helmet.cfg";
        
        public bool enabled = true;
        public MyGraphicsRenderer renderer = MyGraphicsRenderer.DX9;
        public string helmetModel = "vignette";
        public bool hud = true;
        public bool hudAlways = false;
        public bool glass = true;
        public double animateSpeed = 0.3;
        public bool autoFovScale = false;
        public double scale = 0.0;
        public double hudScale = 0.0;
        public float warnBlinkTime = 0.25f;
        public float delayedRotation = 0.5f;
        public bool toggleHelmetInCockpit = false;
        
        public MyGraphicsRenderer prevRenderer = MyGraphicsRenderer.DX9;
        
        public int displayUpdateRate = 20;
        public int displayQuality = 1;
        public Color displayFontColor = new Color(151, 226, 255);
        public Color displayBgColor = new Color(1, 2, 3);
        public Color? displayBorderColor = null;
        public SpeedUnits displaySpeedUnit = SpeedUnits.mps;
        
        public const int TOTAL_ELEMENTS = 12; // NOTE: update Icons class when updating these
        public HudElement[] elements;
        public HudElement[] defaultElements = new HudElement[TOTAL_ELEMENTS]
        {
            new HudElement("warning") { posLeft = 0, posUp = 0.035, },
            new HudElement("health") { posLeft = 0.085, posUp = -0.062, hasBar = true, warnPercent = 15, },
            new HudElement("energy") { posLeft = -0.085, posUp = -0.058, hasBar = true, warnPercent = 15, flipHorizontal = true, },
            new HudElement("oxygen") { posLeft = -0.08, posUp = -0.066, hasBar = true, warnPercent = 15, flipHorizontal = true, },
            new HudElement("oxygenenv") { posLeft = -0.08, posUp = -0.066, },
            new HudElement("hydrogen") { posLeft = -0.075, posUp = -0.074, hasBar = true, warnPercent = 15, warnMoveMode = 2, flipHorizontal = true, },
            new HudElement("inventory") { posLeft = 0.075, posUp = -0.07, hasBar = true, },
            new HudElement("broadcasting") { posLeft = 0.082, posUp = -0.077, },
            new HudElement("dampeners") { posLeft = 0.074, posUp = -0.076, },
            new HudElement("vector") { posLeft = 0, posUp = -0.048, },
            new HudElement("display") { show = 3, posLeft = 0, posUp = -0.07, hudMode = 2, },
            new HudElement("horizon") { hudMode = 2, hasBar = true, },
        };
        
        //public string[] helmetModels = new string[] { "off", "vignette", "helmetmesh" };
        
        public const double MIN_SCALE = -1.0f;
        public const double MAX_SCALE = 1.0f;
        
        public const double MIN_HUDSCALE = -1.0f;
        public const double MAX_HUDSCALE = 1.0f;
        
        public const float MIN_DELAYED = 0.0f;
        public const float MAX_DELAYED = 1.0f;
        
        public const int MIN_DISPLAYUPDATE = 1;
        public const int MAX_DISPLAYUPDATE = 60;
        
        public const string DX9_PREFIX = "DX9_";
        
        private static char[] CHARS = new char[] { '=' };
        
        public bool firstLoad = false;
        
        public Settings()
        {
            // copy defaults over to the usable element data
            elements = new HudElement[TOTAL_ELEMENTS];
            
            for(int i = 0; i < TOTAL_ELEMENTS; i++)
            {
                elements[i] = defaultElements[i].Copy();
            }
            
            // load the settings if they exist
            if(!Load())
            {
                firstLoad = true; // config didn't exist, assume it's the first time the mod is loaded
                ScaleForFOV(MathHelper.ToDegrees(MyAPIGateway.Session.Config.FieldOfView)); // automatically set the scale according to player's FOV
                renderer = prevRenderer = MyAPIGateway.Session.Config.GraphicsRenderer; // automatically set the right renderer
            }
            else
            {
                var r = MyAPIGateway.Session.Config.GraphicsRenderer;
                
                if(r != prevRenderer)
                {
                    renderer = prevRenderer = r;
                }
            }
            
            Save(); // refresh config in case of any missing or extra settings
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
                MyGraphicsRenderer r;
                SpeedUnits u;
                string[] rgb;
                byte red,green,blue;
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
                        Log.Error("Unknown "+FILE+" line: "+line+"\nMaybe is missing the '=' ?");
                        continue;
                    }
                    
                    if(lookForIndentation && args[0].StartsWith("  "))
                    {
                        args[0] = args[0].Trim().ToLower();
                        args[1] = args[1].Trim().ToLower();
                        
                        if(currentId == Icons.HORIZON && args[0] != "hudmode")
                            continue;
                        
                        if(currentId == Icons.DISPLAY)
                        {
                            switch(args[0])
                            {
                                case "update":
                                    if(int.TryParse(args[1], out i))
                                        displayUpdateRate = Math.Min(Math.Max(i, MIN_DISPLAYUPDATE), MAX_DISPLAYUPDATE);
                                    else
                                        Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                    continue;
                                case "quality":
                                    if(int.TryParse(args[1], out i))
                                        displayQuality = MathHelper.Clamp(i, 0, 1);
                                    else
                                        Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                    continue;
                                case "speedunit":
                                    if(Enum.TryParse<SpeedUnits>(args[1], out u))
                                        displaySpeedUnit = u;
                                    else
                                        Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                    continue;
                                case "fontcolor":
                                case "bgcolor":
                                case "bordercolor":
                                    if(args[0] == "bordercolor" && args[1] == "suit")
                                    {
                                        displayBorderColor = null;
                                    }
                                    else
                                    {
                                        rgb = args[1].Split(',');
                                        if(rgb.Length >= 3 && byte.TryParse(rgb[0].Trim(), out red) && byte.TryParse(rgb[1].Trim(), out green) && byte.TryParse(rgb[2].Trim(), out blue))
                                        {
                                            switch(args[0])
                                            {
                                                case "fontcolor":
                                                    displayFontColor = new Color(red, green, blue);
                                                    break;
                                                case "bgcolor":
                                                    displayBgColor = new Color(red, green, blue);
                                                    break;
                                                case "bordercolor":
                                                    displayBorderColor = new Color(red, green, blue);
                                                    break;
                                            }
                                            continue;
                                        }
                                        Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                    }
                                    continue;
                            }
                        }
                        
                        switch(args[0])
                        {
                            case "up":
                                if(double.TryParse(args[1], out d))
                                    elements[currentId].posUp = d;
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                            case "left":
                                if(double.TryParse(args[1], out d))
                                    elements[currentId].posLeft = d;
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                            case "warnpercent":
                                if(int.TryParse(args[1], out i))
                                    elements[currentId].warnPercent = i;
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                            case "warnmovemode":
                                if(int.TryParse(args[1], out i))
                                    elements[currentId].warnMoveMode = i;
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                            case "hudmode":
                                if(int.TryParse(args[1], out i))
                                    elements[currentId].hudMode = i;
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
                            case "renderer":
                                if(Enum.TryParse<MyGraphicsRenderer>(args[1].ToUpper(), out r))
                                    renderer = r;
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                            case "prevrenderer":
                                if(Enum.TryParse<MyGraphicsRenderer>(args[1].ToUpper(), out r))
                                    prevRenderer = r;
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                            case "hud":
                                if(bool.TryParse(args[1], out b))
                                    hud = b;
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                            case "hudalways":
                                if(bool.TryParse(args[1], out b))
                                    hudAlways = b;
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                            case "glass":
                                if(bool.TryParse(args[1], out b))
                                    glass = b;
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                            case "animatespeed":
                                if(float.TryParse(args[1], out f))
                                    animateSpeed = Math.Round(MathHelper.Clamp(f, 0, 10), 5);
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
                            case "delayedrotation":
                                if(float.TryParse(args[1], out f))
                                    delayedRotation = Math.Min(Math.Max(f, MIN_DELAYED), MAX_DELAYED);
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                            case "togglehelmetincockpit":
                                if(bool.TryParse(args[1], out b))
                                    toggleHelmetInCockpit = b;
                                else
                                    Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                continue;
                        }
                        
                        for(int id = 0; id < TOTAL_ELEMENTS; id++)
                        {
                            if(args[0] == elements[id].name)
                            {
                                currentId = id;
                                lookForIndentation = true;
                                
                                if(int.TryParse(args[1], out i))
                                {
                                    elements[currentId].show = i;
                                }
                                else
                                {
                                    if(bool.TryParse(args[1], out b)) // backwards compatible with the old true/false setting
                                        elements[currentId].show = (b ? defaultElements[currentId].show : 0);
                                    else
                                        Log.Error("Invalid "+args[0]+" value: " + args[1]);
                                }
                                
                                break;
                            }
                        }
                        
                        if(lookForIndentation)
                            continue;
                    }
                    
                    //Log.Error("Unknown setting: " + args[0]);
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
        
        private string boolToLower(bool b)
        {
            return b ? "true" : "false";
        }
        
        public string GetSettingsString(bool comments)
        {
            var str = new StringBuilder();
            
            if(comments)
            {
                str.AppendLine("// Helmet mod config; this file gets automatically overwritten after being loaded so don't leave custom comments.");
                str.AppendLine("// You can reload this while the game is running by typing in chat: /helmet reload");
                str.AppendLine("// Lines starting with // are comments");
                str.AppendLine();
            }
            
            str.Append("Enabled=").Append(boolToLower(enabled)).AppendLine(comments ? " // enable the mod ?" : "");
            str.Append("Renderer=").Append(renderer).AppendLine(comments ? " // renderer used in-game, options: DX9, DX11" : "");
            str.Append("Hud=").Append(boolToLower(hud)).AppendLine(comments ? " // enable the HUD ?" : "");
            str.Append("HudAlways=").Append(boolToLower(hudAlways)).AppendLine(comments ? " // toggle if the HUD shows even if the helmet is off, default: false" : "");
            str.Append("Glass=").Append(boolToLower(glass)).AppendLine(comments ? " // enable the reflective glass ?" : "");
            str.Append("AnimateSpeed=").Append(animateSpeed).AppendLine(comments ? " // helmet animation speed, 0 to disable, default: 0.3" : "");
            str.Append("AutoFOVScale=").Append(boolToLower(autoFovScale)).AppendLine(comments ? " // if true it automatically sets 'scale' and 'hudscale' when changing FOV" : "");
            str.Append("Scale=").Append(scale).AppendLine(comments ? " // the helmet glass scale, -1.0 to 1.0, default 0 for FOV 60" : "");
            str.Append("HudScale=").Append(hudScale).AppendLine(comments ? " // the entire HUD scale, -1.0 to 1.0, default 0 for FOV 60" : "");
            str.Append("WarnBlinkTime=").Append(warnBlinkTime).AppendLine(comments ? " // the time between each hide/show of the warning icon and its respective bar" : "");
            str.Append("DelayedRotation=").Append(delayedRotation).AppendLine(comments ? " // 0.0 to 1.0, how much to delay the helmet when rotating view, 0 disables it" : "");
            str.Append("ToggleHelmetInCockpit=").Append(boolToLower(toggleHelmetInCockpit)).AppendLine(comments ? " // enable toggling helmet inside a cockpit? WARNING: pressing the key while in chat or certain menus will also toggle the helmet. Default: false" : "");
            
            if(comments)
            {
                str.AppendLine();
                str.AppendLine("// Individual HUD element configuration (advanced)");
            }
            
            for(int id = 0; id < TOTAL_ELEMENTS; id++)
            {
                var element = elements[id];
                var defaultElement = defaultElements[id];
                
                str.Append(element.name).Append("=").Append(element.show).AppendLine(comments ? " // when to show this element, 0 = never, 1 only when helmet is ON, 2 = only when helmet is OFF, 3 = always" : "");
                
                if(id != Icons.HORIZON)
                {
                    str.Append("  Up=").Append(element.posUp).AppendLine(comments ? " // position from the center towards up, use negative values for down; default: "+defaultElement.posUp : "");
                    str.Append("  Left=").Append(element.posLeft).AppendLine(comments ? " // position from the center towards left, use negative values for right; default: "+defaultElement.posLeft : "");
                }
                
                str.Append("  HudMode=").Append(element.hudMode).AppendLine(comments ? " // shows icon depending on the vanilla HUD's state: 0 = any, 1 = shown when HUD is visible, 2 = shown when HUD is hidden; default: "+defaultElement.hudMode : "");
                
                if(id != Icons.HORIZON)
                {
                    if(id == Icons.DISPLAY)
                    {
                        str.Append("  Updaterate=").Append(displayUpdateRate).AppendLine(comments ? " // updates per second, 1 to 60 (depends on simulation speed), default 20" : "");
                        str.Append("  Quality=").Append(displayQuality).AppendLine(comments ? " // texture size and model detail, default 1 (512x512 with details), set to 0 for 256x256 without model details" : "");
                        str.Append("  Fontcolor=").Append(displayFontColor.R).Append(",").Append(displayFontColor.G).Append(",").Append(displayFontColor.B).AppendLine(comments ? " // text color in R,G,B format, default 151,226,255" : "");
                        str.Append("  BGColor=").Append(displayBgColor.R).Append(",").Append(displayBgColor.G).Append(",").Append(displayBgColor.B).AppendLine(comments ? " // background color in R,G,B format, default 1,2,3" : "");
                        
                        str.Append("  BorderColor=");
                        if(displayBorderColor.HasValue)
                            str.Append(displayBorderColor.Value.R).Append(",").Append(displayBorderColor.Value.G).Append(",").Append(displayBorderColor.Value.B);
                        else
                            str.Append("suit");
                        str.AppendLine(comments ? " // LCD frame color in R,G,B format or \"suit\" to use the suit's color, default: suit" : "");
                        
                        str.Append("  SpeedUnit=").Append(displaySpeedUnit).AppendLine(comments ? " // unit displayed for speed, options: "+String.Join(", ", Enum.GetNames(typeof(SpeedUnits))) : "");
                    }
                    
                    if(defaultElement.warnPercent > -1)
                    {
                        str.Append("  WarnPercent=").Append(element.warnPercent).AppendLine(comments ? " // warning % for this statistic; default: "+defaultElement.warnPercent : "");
                        str.Append("  WarnMoveMode=").Append(element.warnMoveMode).AppendLine(comments ? " // warning only shows in a mode: 0 = any, 1 = jetpack off, 2 = jetpack on; default: "+defaultElement.warnMoveMode : "");
                    }
                }
            }
            
            if(comments)
                str.AppendLine().AppendLine().AppendLine();
            
            str.Append("PrevRenderer=").Append(prevRenderer).AppendLine(comments ? " // DO NOT EDIT! Used to reset renderer if you change it in-game." : "");

            return str.ToString();
        }
        
        public string GetRenderPrefix()
        {
            return renderer == MyGraphicsRenderer.DX9 ? DX9_PREFIX : "";
        }
        
        public string GetHelmetModel()
        {
            return helmetModel + (glass ? "" : "NoReflection");
        }
        
        public void Close()
        {
        }
    }
}