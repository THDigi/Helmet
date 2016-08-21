using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Sandbox.ModAPI;
using VRageMath;
using Digi.Utils;

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
        public string material = null;

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
                material = this.material,
            };
        }
    }

    public enum SpeedUnits
    {
        mps,
        kph,
    }

    public enum HUDQualityEnum
    {
        verylow = 0,
        low,
        medium,
        high,
        ultra
    }

    public static class Icons // change TOTAL_ELEMENTS and defaultElements when you add/remove to this
    {
        public const int WARNING = 0;
        public const int HEALTH = 1;
        public const int ENERGY = 2;
        public const int OXYGEN = 3;
        public const int OXYGEN_ENV = 4;
        public const int HYDROGEN = 5;
        public const int INVENTORY = 6;
        public const int THRUSTERS = 7;
        public const int DAMPENERS = 8;
        public const int LIGHTS = 9;
        public const int BROADCASTING = 10;
        public const int VECTOR = 11;
        public const int DISPLAY = 12;
        public const int HORIZON = 13;
        public const int CROSSHAIR = 14;
        public const int MARKERS = 15;
    };

    public class Settings
    {
        private const string FILE = "helmet.cfg";

        public bool enabled = true;
        public string helmetModel = "vignette";
        public bool hud = true;
        public HUDQualityEnum hudQuality = HUDQualityEnum.high;
        public bool hudAlways = false;
        public bool glassReflections = true;
        public double animateTime = 0.3;
        public bool autoFovScale = false;
        public double scale = 0.0;
        public double hudScale = 0.0;
        public float warnBlinkTime = 0.25f;
        public float delayedRotation = 0.5f;
        public bool toggleHelmetInCockpit = false;

        // TODO feature: lights
        //public byte lightReplace = 1;
        //public byte lightBeams = 2;
        //public byte lightDustParticles = 2;

        public string crosshairType = "extended";
        public Color crosshairColor = new Color(0, 55, 255);
        public float crosshairScale = 0.75f;
        public float crosshairSwayRatio = 0.1f;

        public bool markerShowGPS = true;
        public bool markerShowAntennas = true;
        public bool markerShowBeacons = true;
        public bool markerShowBlocks = true;
        public float markerScale = 1f;
        public Color markerColorGPS = Color.Purple;
        public Color markerColorOwned = new Color(0, 55, 255);
        public Color markerColorFaction = Color.Green;
        public Color markerColorEnemy = Color.Red;
        public Color markerColorNeutral = Color.White;
        public Color markerColorBlock = Color.Yellow;

        public int displayUpdateRate = 20;
        public int displayQuality = 1;
        public Color displayFontColor = new Color(151, 226, 255);
        public Color displayBgColor = new Color(1, 2, 3);
        public Color? displayBorderColor = null;
        public SpeedUnits displaySpeedUnit = SpeedUnits.mps;

        public const int TOTAL_ELEMENTS = 16; // NOTE: update Icons class when updating these
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
            new HudElement("thrusters") { posLeft = 0.084, posUp = -0.077, material = "HelmetHUDIcon_Thrusters", },
            new HudElement("dampeners") { posLeft = 0.078, posUp = -0.076, material = "HelmetHUDIcon_Dampeners", },
            new HudElement("lights") { posLeft = 0.072, posUp = -0.075, material = "HelmetHUDIcon_Lights", },
            new HudElement("broadcasting") { posLeft = 0.066, posUp = -0.074, material = "HelmetHUDIcon_Broadcasting", },
            new HudElement("vector") { posLeft = 0, posUp = -0.048, },
            new HudElement("display") { show = 3, hudMode = 2, posLeft = 0, posUp = -0.07, },
            new HudElement("horizon") { hudMode = 2, hasBar = true, },
            new HudElement("crosshair") { hudMode = 2, },
            new HudElement("markers") { hudMode = 2, },
        };

        public Dictionary<string, string> crosshairTypes = new Dictionary<string, string>()
        {
            { "vanilla", "HelmetCrosshairVanilla" },
            { "extended", "HelmetCrosshairExtended" },
            { "dot", "HelmetCrosshairDot" },
        };

        public const double MIN_SCALE = -1.0f;
        public const double MAX_SCALE = 1.0f;

        public const double MIN_HUDSCALE = -1.0f;
        public const double MAX_HUDSCALE = 1.0f;

        public const float MIN_DELAYED = 0.0f;
        public const float MAX_DELAYED = 1.0f;

        public const int MIN_DISPLAYUPDATE = 1;
        public const int MAX_DISPLAYUPDATE = 60;

        private static char[] CHARS = new char[] { '=' };

        public bool firstLoad = false;

        private bool resetBroadcastDampenersPos = true; // assuming that thruster and light settings don't exist and set this false when they're found

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
                SpeedUnits u;
                HUDQualityEnum q;
                string[] rgb;
                byte red, green, blue, alpha;
                bool lookForIndentation = false;
                int currentId = -1;

                while((line = file.ReadLine()) != null)
                {
                    if(line.Length == 0)
                        continue;

                    i = line.IndexOf("//", StringComparison.Ordinal);

                    if(i > -1)
                        line = (i == 0 ? "" : line.Substring(0, i));

                    if(line.Length == 0)
                        continue;

                    args = line.Split(CHARS, 2);

                    if(args.Length != 2)
                    {
                        Log.Error("Unknown " + FILE + " line: " + line + "\nMaybe is missing the '=' ?");
                        continue;
                    }

                    if(lookForIndentation && args[0].StartsWith("  ", StringComparison.Ordinal))
                    {
                        args[0] = args[0].Trim().ToLower();
                        args[1] = args[1].Trim().ToLower();

                        if(currentId == Icons.HORIZON && args[0] != "hudmode")
                            continue;

                        if(currentId == Icons.THRUSTERS || currentId == Icons.LIGHTS)
                            resetBroadcastDampenersPos = false; // config already had these settings so the dampeners and broadcast ones were already reset

                        if(currentId == Icons.DISPLAY)
                        {
                            switch(args[0])
                            {
                                case "update":
                                    if(int.TryParse(args[1], out i))
                                        displayUpdateRate = Math.Min(Math.Max(i, MIN_DISPLAYUPDATE), MAX_DISPLAYUPDATE);
                                    else
                                        Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                    continue;
                                case "quality":
                                    if(int.TryParse(args[1], out i))
                                        displayQuality = MathHelper.Clamp(i, 0, 1);
                                    else
                                        Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                    continue;
                                case "speedunit":
                                    if(Enum.TryParse<SpeedUnits>(args[1], out u))
                                        displaySpeedUnit = u;
                                    else
                                        Log.Error("Invalid " + args[0] + " value: " + args[1]);
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
                                        Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                    }
                                    continue;
                            }
                        }

                        if(currentId == Icons.CROSSHAIR)
                        {
                            switch(args[0])
                            {
                                case "type":
                                    if(crosshairTypes.ContainsKey(args[1]))
                                        crosshairType = args[1];
                                    else
                                        Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                    continue;
                                case "color":
                                    rgb = args[1].Split(',');
                                    if(rgb.Length >= 4 && byte.TryParse(rgb[0].Trim(), out red) && byte.TryParse(rgb[1].Trim(), out green) && byte.TryParse(rgb[2].Trim(), out blue) && byte.TryParse(rgb[3].Trim(), out alpha))
                                        crosshairColor = new Color(red, green, blue, alpha);
                                    else
                                        Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                    continue;
                                case "scale":
                                    if(float.TryParse(args[1], out f))
                                        crosshairScale = MathHelper.Clamp(f, 0.0001f, 10f);
                                    else
                                        Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                    continue;
                                case "swayratio":
                                    if(float.TryParse(args[1], out f))
                                        crosshairSwayRatio = MathHelper.Clamp(f, 0, 1);
                                    else
                                        Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                    continue;
                            }
                        }

                        if(currentId == Icons.MARKERS)
                        {
                            switch(args[0])
                            {
                                case "showgps":
                                    if(bool.TryParse(args[1], out b))
                                        markerShowGPS = b;
                                    else
                                        Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                    continue;
                                case "showantennas":
                                    if(bool.TryParse(args[1], out b))
                                        markerShowAntennas = b;
                                    else
                                        Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                    continue;
                                case "showbeacons":
                                    if(bool.TryParse(args[1], out b))
                                        markerShowBeacons = b;
                                    else
                                        Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                    continue;
                                case "showblocks":
                                    if(bool.TryParse(args[1], out b))
                                        markerShowBlocks = b;
                                    else
                                        Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                    continue;
                                case "scale":
                                    if(float.TryParse(args[1], out f))
                                        markerScale = MathHelper.Clamp(f, 0.001f, 10f);
                                    else
                                        Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                    continue;
                                case "colorgps":
                                case "colorowned":
                                case "colorfaction":
                                case "colorenemy":
                                case "colorneutral":
                                case "colorblock":
                                    rgb = args[1].Split(',');
                                    if(rgb.Length >= 4 && byte.TryParse(rgb[0].Trim(), out red) && byte.TryParse(rgb[1].Trim(), out green) && byte.TryParse(rgb[2].Trim(), out blue) && byte.TryParse(rgb[3].Trim(), out alpha))
                                    {
                                        var c = new Color(red, green, blue, alpha);
                                        switch(args[0])
                                        {
                                            case "colorgps": markerColorGPS = c; break;
                                            case "colorowned": markerColorOwned = c; break;
                                            case "colorfaction": markerColorFaction = c; break;
                                            case "colorenemy": markerColorEnemy = c; break;
                                            case "colorneutral": markerColorNeutral = c; break;
                                            case "colorblock": markerColorBlock = c; break;
                                        }
                                    }
                                    else
                                        Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                    continue;
                            }
                        }

                        switch(args[0])
                        {
                            case "up":
                                if(double.TryParse(args[1], out d))
                                    elements[currentId].posUp = d;
                                else
                                    Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                continue;
                            case "left":
                                if(double.TryParse(args[1], out d))
                                    elements[currentId].posLeft = d;
                                else
                                    Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                continue;
                            case "warnpercent":
                                if(int.TryParse(args[1], out i))
                                    elements[currentId].warnPercent = i;
                                else
                                    Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                continue;
                            case "warnmovemode":
                                if(int.TryParse(args[1], out i))
                                    elements[currentId].warnMoveMode = i;
                                else
                                    Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                continue;
                            case "hudmode":
                                if(int.TryParse(args[1], out i))
                                    elements[currentId].hudMode = i;
                                else
                                    Log.Error("Invalid " + args[0] + " value: " + args[1]);
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
                                    Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                continue;
                            case "hud":
                                if(bool.TryParse(args[1], out b))
                                    hud = b;
                                else
                                    Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                continue;
                            case "hudquality":
                                if(Enum.TryParse<HUDQualityEnum>(args[1], out q))
                                    hudQuality = q;
                                else
                                    Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                continue;
                            case "hudalways":
                                if(bool.TryParse(args[1], out b))
                                    hudAlways = b;
                                else
                                    Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                continue;
                            case "glass": // backwards compatibility
                            case "glassreflections":
                                if(bool.TryParse(args[1], out b))
                                    glassReflections = b;
                                else
                                    Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                continue;
                            case "animatespeed": // backwards compatibility
                            case "animatetime":
                                if(float.TryParse(args[1], out f))
                                    animateTime = Math.Round(MathHelper.Clamp(f, 0, 3), 5);
                                else
                                    Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                continue;
                            case "autofovscale":
                                if(bool.TryParse(args[1], out b))
                                    autoFovScale = b;
                                else
                                    Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                continue;
                            case "scale":
                                if(double.TryParse(args[1], out d))
                                    scale = Math.Min(Math.Max(d, MIN_SCALE), MAX_SCALE);
                                else
                                    Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                continue;
                            case "hudscale":
                                if(double.TryParse(args[1], out d))
                                    hudScale = Math.Min(Math.Max(d, MIN_HUDSCALE), MAX_HUDSCALE);
                                else
                                    Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                continue;
                            case "warnblinktime":
                                if(float.TryParse(args[1], out f))
                                    warnBlinkTime = f;
                                else
                                    Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                continue;
                            case "delayedrotation":
                                if(float.TryParse(args[1], out f))
                                    delayedRotation = Math.Min(Math.Max(f, MIN_DELAYED), MAX_DELAYED);
                                else
                                    Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                continue;
                            case "togglehelmetincockpit":
                                if(bool.TryParse(args[1], out b))
                                    toggleHelmetInCockpit = b;
                                else
                                    Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                continue;

                                // TODO feature: lights
                                //case "lightreplace":
                                //    if(int.TryParse(args[1], out i))
                                //        lightReplace = (byte)MathHelper.Clamp(i, 0, 2);
                                //    else
                                //        Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                //    continue;
                                //case "lightbeams":
                                //    if(int.TryParse(args[1], out i))
                                //        lightBeams = (byte)MathHelper.Clamp(i, 0, 2);
                                //    else
                                //        Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                //    continue;
                                //case "lightdustparticles":
                                //    if(int.TryParse(args[1], out i))
                                //        lightDustParticles = (byte)MathHelper.Clamp(i, 0, 2);
                                //    else
                                //        Log.Error("Invalid " + args[0] + " value: " + args[1]);
                                //    continue;
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
                                        Log.Error("Invalid " + args[0] + " value: " + args[1]);
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

                if(resetBroadcastDampenersPos)
                {
                    elements[Icons.BROADCASTING].posLeft = defaultElements[Icons.BROADCASTING].posLeft;
                    elements[Icons.BROADCASTING].posUp = defaultElements[Icons.BROADCASTING].posUp;
                    elements[Icons.DAMPENERS].posLeft = defaultElements[Icons.DAMPENERS].posLeft;
                    elements[Icons.DAMPENERS].posUp = defaultElements[Icons.DAMPENERS].posUp;

                    Log.Info("NOTE: The broadcasting and dampeners HUD elements' positions have been reset because the lights and thruster elements have been added and required room.");
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void ScaleForFOV(float fov)
        {
            double fovScale = ((Math.Min(Math.Max(fov, 40), 140) - 60) / 30.0);

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

            str.Append("Enabled=").Append(boolToLower(enabled)).AppendLine(comments ? " // toggles the entire mod, default: true" : "");
            str.Append("HUD=").Append(boolToLower(hud)).AppendLine(comments ? " // toggles the HUD, default: true" : "");
            str.Append("HUDQuality=").Append(hudQuality).AppendLine(comments ? " // controls the quality of certain HUD elements, currently affects the vector indicator. Values: verylow, low, medum, high, ultra, default: high. Ultra is not noticeable comapred to high on 1080p" : "");
            str.Append("HUDAlways=").Append(boolToLower(hudAlways)).AppendLine(comments ? " // if set to true, all HUD elements will be shown even if the helmet is off (set to 3) with the exception of disabled elements (the ones set to 0), default: false" : "");
            str.Append("GlassReflections=").Append(boolToLower(glassReflections)).AppendLine(comments ? " // toggles the reflections on the helmet, default: true" : "");
            str.Append("AnimateTime=").Append(animateTime).AppendLine(comments ? " // helmet animation time in seconds, 0 to disable, default: 0.3" : "");
            str.Append("AutoFOVScale=").Append(boolToLower(autoFovScale)).AppendLine(comments ? " // if true it automatically sets 'scale' and 'hudscale' when changing FOV" : "");
            str.Append("Scale=").Append(scale).AppendLine(comments ? " // the helmet glass scale, -1.0 to 1.0, default 0 for FOV 60" : "");
            str.Append("HudScale=").Append(hudScale).AppendLine(comments ? " // the entire HUD scale, -1.0 to 1.0, default 0 for FOV 60" : "");
            str.Append("WarnBlinkTime=").Append(warnBlinkTime).AppendLine(comments ? " // the time between each hide/show of the warning icon and its respective bar" : "");
            str.Append("DelayedRotation=").Append(delayedRotation).AppendLine(comments ? " // 0.0 to 1.0, how much to delay the helmet when rotating view, 0 disables it" : "");
            str.Append("ToggleHelmetInCockpit=").Append(boolToLower(toggleHelmetInCockpit)).AppendLine(comments ? " // enable toggling helmet inside a cockpit. WARNING: the key monitoring still works while in menus so be aware of that before enabling this. Default: false" : "");

            // TODO feature: lights
            //if(comments)
            //    str.AppendLine();
            //
            //str.Append("LightReplace=").Append(lightReplace).AppendLine(comments ? " // replaces the vanilla headlamp which is currently badly tweaked with a better one. 0 = vanilla, 1 = replace with 2 light sources (realistic). Default: 1" : "");
            //str.Append("LightBeams=").Append(lightBeams).AppendLine(comments ? " // (works only if LightReplace is not 0) adds subtle light beams in first person view when the headlamp is turned on. 0 = disabled, 1 = enabled always at full intensity, 2 = intensity depends on air density (realistic). Default: 2" : "");
            //str.Append("LightDustParticles=").Append(lightDustParticles).AppendLine(comments ? " // (works only if LightReplace and LightBeams are not 0) dust particles in the light beams of your helmet. 0 = disabled, 1 = 15 particles, 2 = 30 particles. Default: 2" : "");

            if(comments)
            {
                str.AppendLine();
                str.AppendLine("// Individual HUD element configuration");
            }

            for(int id = 0; id < TOTAL_ELEMENTS; id++)
            {
                var element = elements[id];
                var defaultElement = defaultElements[id];

                str.Append(element.name.ToUpperFirst()).Append("=").Append(element.show).AppendLine(comments ? " // when to show this element, 0 = never, 1 only when helmet is ON, 2 = only when helmet is OFF, 3 = always" : "");

                if(!(id == Icons.CROSSHAIR || id == Icons.MARKERS || id == Icons.HORIZON))
                {
                    str.Append("  Up=").Append(element.posUp).AppendLine(comments ? " // position from the center towards up, use negative values for down; default: " + defaultElement.posUp : "");
                    str.Append("  Left=").Append(element.posLeft).AppendLine(comments ? " // position from the center towards left, use negative values for right; default: " + defaultElement.posLeft : "");
                }

                str.Append("  HudMode=").Append(element.hudMode).AppendLine(comments ? " // shows icon depending on the vanilla HUD's state: 0 = any, 1 = shown when HUD is visible, 2 = shown when HUD is hidden; default: " + defaultElement.hudMode : "");

                if(id != Icons.HORIZON)
                {
                    if(id == Icons.CROSSHAIR)
                    {
                        str.Append("  Type=").Append(crosshairType).AppendLine(comments ? " // crosshair texture, default: vanilla; other options: " + String.Join(", ", crosshairTypes.Keys) : "");
                        str.Append("  Color=").AppendRGBA(crosshairColor).AppendLine(comments ? " // crosshair color in RGBA format, default: 255, 255, 255, 255" : "");
                        str.Append("  Scale=").Append(Math.Round(crosshairScale, 5)).AppendLine(comments ? " // size of the crosshair, independent of the HUD scale, default: 0.75" : "");
                        str.Append("  SwayRatio=").Append(crosshairSwayRatio).AppendLine(comments ? " // ratio between crosshair being locked to view or to helmet, with 0 being locked to camera and 1 following helmet entirely. Default: 0.1" : "");
                    }

                    if(id == Icons.MARKERS)
                    {
                        str.Append("  ShowGPS=").Append(boolToLower(markerShowGPS)).AppendLine(comments ? " // wether GPS markers are shown, default true" : "");
                        str.Append("  ShowAntennas=").Append(boolToLower(markerShowAntennas)).AppendLine(comments ? " // wether antenna markers are shown, default true" : "");
                        str.Append("  ShowBeacons=").Append(boolToLower(markerShowBeacons)).AppendLine(comments ? " // wether beacon markers are shown, default true" : "");
                        str.Append("  ShowBlocks=").Append(boolToLower(markerShowBlocks)).AppendLine(comments ? " // wether block markers (show in HUD) are shown, default true" : "");
                        str.Append("  Scale=").Append(Math.Round(markerScale, 5)).AppendLine(comments ? " // scales all markers by this value. default: 1.0" : "");
                        str.Append("  ColorGPS=").AppendRGBA(markerColorGPS).AppendLine(comments ? " // the color of the GPS markers in RGBA format, default: 128, 0, 128, 255" : "");
                        str.Append("  ColorOwned=").AppendRGBA(markerColorOwned).AppendLine(comments ? " // the color of your owned signals in RGBA format, default: 0, 55, 255, 255" : "");
                        str.Append("  ColorFaction=").AppendRGBA(markerColorFaction).AppendLine(comments ? " // the color of your faction signals in RGBA format, default: 0, 128, 0, 255" : "");
                        str.Append("  ColorEnemy=").AppendRGBA(markerColorEnemy).AppendLine(comments ? " // the color of the enemy signals in RGBA format, default: 255, 0, 0, 255" : "");
                        str.Append("  ColorNeutral=").AppendRGBA(markerColorNeutral).AppendLine(comments ? " // the color of the neutral signals in RGBA format, default: 255, 255, 255, 255" : "");
                        str.Append("  ColorBlock=").AppendRGBA(markerColorBlock).AppendLine(comments ? " // the color of block signals in RGBA format, default: 255, 255, 0, 255" : "");
                    }

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

                        str.Append("  SpeedUnit=").Append(displaySpeedUnit).AppendLine(comments ? " // unit displayed for speed, options: " + String.Join(", ", Enum.GetNames(typeof(SpeedUnits))) : "");
                    }

                    if(defaultElement.warnPercent > -1)
                    {
                        str.Append("  WarnPercent=").Append(element.warnPercent).AppendLine(comments ? " // warning % for this statistic; default: " + defaultElement.warnPercent : "");
                        str.Append("  WarnMoveMode=").Append(element.warnMoveMode).AppendLine(comments ? " // warning only shows in a mode: 0 = any, 1 = jetpack off, 2 = jetpack on; default: " + defaultElement.warnMoveMode : "");
                    }
                }
            }

            return str.ToString();
        }

        public string GetHelmetModel()
        {
            return (glassReflections ? helmetModel : helmetModel + "NoReflection");
        }

        public void Close()
        {
        }
    }
}