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
using VRageMath;
using Digi.Utils;

namespace Digi.Helmet
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class Helmet : MySessionComponentBase
    {
        public bool init { get; private set; }
        public bool isServer { get; private set; }
        public bool isDedicated { get; private set; }
        public Settings settings { get; private set; }
        private IMyEntity helmet = null;
        private IMyEntity characterEntity = null;
        private bool removedHelmet = false;
        private bool removedHUD = false;
        private bool lastState = false;
        private long lastReminder;
        private bool warningBlinkOn = true;
        private double lastWarningBlink = 0;
        
        public IMyEntity[] iconEntities = new IMyEntity[Settings.TOTAL_ICONS];
        public IMyEntity[] iconBarEntities = new IMyEntity[Settings.TOTAL_ICONS];
        
        /*
        private const int HEALTH = 0;
        private const int ENERGY = 1;
        private const int OXYGEN = 2;
        private const int INVENTORY = 3;
        private const int TOTAL = 4;
        private string[] HudNames = new string[] { "Health", "Energy", "Oxygen", "Inventory" };
        private double[] IconPosLeft = new double[] { 0.085, -0.075, 0.075, -0.085 };
        private double[] IconPosTop = new double[] { -0.062, -0.07, -0.07, -0.062 };
        private bool[] BarAlignLeft = new bool[] { false, true, false, true };
        private int[] HudWarningPercent = new int[] { 15, 15, 15, -1 };
        private IMyEntity[] HudIcons = new IMyEntity[TOTAL];
        private IMyEntity[] HudBars = new IMyEntity[TOTAL];
        private IMyEntity HudBroadcasting = null;
        private IMyEntity HudDampeners = nufll;
        private IMyEntity HudWarning = null;
         */
        
        private const int HUD_BAR_MAX_SCALE = 380;
        private const float SCALE_DIST_ADJUST = 0.1f;
        private const float HUDSCALE_DIST_ADJUST = 0.1f;
        
        private const string HELMET_PREFAB = "helmet";
        private const string MOD_NAME = "Helmet";
        
        public void Init()
        {
            init = true;
            isServer = MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE || MyAPIGateway.Multiplayer.IsServer;
            isDedicated = (MyAPIGateway.Utilities.IsDedicated && isServer);
            
            if(isDedicated)
                return;
            
            settings = new Settings();
            MyAPIGateway.Utilities.MessageEntered += MessageEntered;
        }
        
        protected override void UnloadData()
        {
            init = false;
            MyAPIGateway.Utilities.MessageEntered -= MessageEntered;
            
            if(settings != null)
            {
                settings.Close();
                settings = null;
            }
            
            Log.Close();
        }
        
        public override void UpdateAfterSimulation()
        {
            //var timer = new VRage.Library.Utils.MyGameTimer();
            Update();
            //MyAPIGateway.Utilities.ShowNotification(String.Format("elapsed={0:0.00000}ms", timer.Elapsed.Miliseconds), 16, MyFontEnum.Green);
            //result: ~0.05ms
        }
        
        private void Update()
        {
            if(isDedicated)
                return;
            
            if(!init)
            {
                if(MyAPIGateway.Session == null)
                    return;
                
                Init();
                
                if(isDedicated)
                    return;
            }
            
            if(!settings.enabled)
            {
                RemoveHelmet();
                return;
            }
            
            if(settings.reminder)
            {
                long now = DateTime.UtcNow.Ticks;
                
                if(now > (lastReminder + (TimeSpan.TicksPerSecond * 60)))
                {
                    MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Type /helmet in chat to configure your helmet (will also remove this notice permanently)");
                    lastReminder = now;
                }
            }
            
            if(characterEntity != null && (characterEntity.MarkedForClose || characterEntity.Closed))
                characterEntity = null;
            
            if(MyAPIGateway.Session.Player != null
               && MyAPIGateway.Session.Player.Controller != null
               && MyAPIGateway.Session.Player.Controller.ControlledEntity != null
               && MyAPIGateway.Session.Player.Controller.ControlledEntity.Entity != null
               && MyAPIGateway.Session.CameraController != null
               && MyAPIGateway.Session.CameraController.IsInFirstPersonView)
            {
                var controlled = MyAPIGateway.Session.Player.Controller.ControlledEntity.Entity;
                
                if(MyAPIGateway.Session.CameraController == controlled)
                {
                    if(controlled is IMyCharacter)
                    {
                        characterEntity = controlled;
                        
                        switch(AttachHelmet(characterEntity))
                        {
                            case 2:
                                return;
                        }
                        
                        lastState = false;
                    }
                    else if(lastState && (controlled is Sandbox.ModAPI.Ingame.IMyCockpit || controlled is Sandbox.ModAPI.Ingame.IMyRemoteControl))
                    {
                        if(characterEntity != null)
                        {
                            switch(AttachHelmet(characterEntity))
                            {
                                case 2:
                                    return;
                                case 1:
                                    RemoveHelmet();
                                    return;
                            }
                        }
                        
                        if(controlled is Sandbox.ModAPI.Ingame.IMyCockpit)
                        {
                            UpdateHelmetAt(MyAPIGateway.Session.ControlledObject.GetHeadMatrix(true, true), null);
                            return;
                        }
                    }
                }
            }
            
            RemoveHelmet();
        }
        
        private int AttachHelmet(IMyEntity charEnt)
        {
            var character = charEnt.GetObjectBuilder(false) as MyObjectBuilder_Character;
            
            if(character != null && character.CharacterModel != null)
            {
                MyCharacterDefinition def;
                
                if(MyDefinitionManager.Static.Characters.TryGetValue(character.CharacterModel, out def) ? !def.NeedsOxygen : !character.CharacterModel.EndsWith("_no_helmet"))
                {
                    UpdateHelmetAt(MyAPIGateway.Session.ControlledObject.GetHeadMatrix(true, true), character);
                    return 2;
                }
                
                return 1;
            }
            
            return 0;
        }
        
        private void RemoveHelmet()
        {
            if(removedHelmet)
                return;
            
            removedHelmet = true;
            
            if(helmet != null)
            {
                helmet.Close();
                helmet = null;
            }
            
            RemoveHud();
        }
        
        private void RemoveHud()
        {
            if(removedHUD)
                return;
            
            removedHUD = true;
            
            for(int id = 0; id < Settings.TOTAL_ICONS; id++)
            {
                if(iconEntities[id] != null)
                {
                    iconEntities[id].Close();
                    iconEntities[id] = null;
                }
                
                if(iconBarEntities[id] != null)
                {
                    iconBarEntities[id].Close();
                    iconBarEntities[id] = null;
                }
            }
        }
        
        private MatrixD helmetMatrix;
        private float[] values = new float[Settings.TOTAL_ICONS];
        private bool[] show = new bool[Settings.TOTAL_ICONS];
        private MatrixD lastMatrix;
        private MatrixD temp;
        private Vector3D pos;
        
        private void UpdateHelmetAt(MatrixD matrix, MyObjectBuilder_Character character)
        {
            if(helmet == null)
            {
                helmet = SpawnPrefab(HELMET_PREFAB);
                
                if(helmet == null)
                    return;
            }
            
            pos = matrix.Translation;
            temp = MatrixD.Lerp(lastMatrix, matrix, (1.0f - settings.delayedRotation));
            lastMatrix = matrix;
            matrix = temp;
            matrix.Translation = pos;
            
            helmetMatrix = matrix;
            helmetMatrix.Translation += matrix.Forward * (SCALE_DIST_ADJUST * (1.0 - settings.scale));
            helmet.SetWorldMatrix(helmetMatrix);
            
            removedHelmet = false;
            lastState = true;
            
            if(!settings.hud || character == null)
            {
                RemoveHud();
                return;
            }
            
            for(int id = 0; id < Settings.TOTAL_ICONS; id++)
            {
                values[id] = 0;
                show[id] = settings.iconShow[id];
            }
            
            values[Icons.HEALTH] = (character.Health.HasValue ? character.Health.Value : 100);
            values[Icons.ENERGY] = (character.Battery != null ? character.Battery.CurrentCapacity * 10000000 : 0);
            values[Icons.OXYGEN] = character.OxygenLevel * 100;
            values[Icons.INVENTORY] = 0;
            
            show[Icons.WARNING] = false;
            show[Icons.BROADCASTING] = character.EnableBroadcasting;
            show[Icons.DAMPENERS] = character.DampenersEnabled;
            
            var invOwner = (characterEntity as Sandbox.ModAPI.Interfaces.IMyInventoryOwner);
            
            if(invOwner != null && invOwner.InventoryCount > 0)
            {
                var inv = invOwner.GetInventory(0);
                
                if(inv != null)
                    values[Icons.INVENTORY] = ((float)inv.CurrentVolume / (float)inv.MaxVolume) * 100;
            }
            
            matrix.Translation += matrix.Forward * (HUDSCALE_DIST_ADJUST * (1.0 - settings.hudScale));
            
            var glassCurve = matrix.Translation + (matrix.Backward * 0.25);
            
            if(settings.iconShow[Icons.WARNING] &&
               (values[Icons.HEALTH] <= settings.iconWarnPercent[Icons.HEALTH]
                || values[Icons.ENERGY] <= settings.iconWarnPercent[Icons.ENERGY]
                || values[Icons.OXYGEN] <= settings.iconWarnPercent[Icons.OXYGEN]))
            {
                double tick = DateTime.UtcNow.Ticks;
                
                if(lastWarningBlink < tick)
                {
                    warningBlinkOn = !warningBlinkOn;
                    lastWarningBlink = tick + (TimeSpan.TicksPerSecond * settings.warnBlinkTime);
                }
                
                show[Icons.WARNING] = true;
            }
            
            for(int id = 0; id < Settings.TOTAL_ICONS; id++)
            {
                UpdateIcon(matrix, glassCurve, id, show[id], values[id]);
            }
            
            removedHUD = false;
        }
        
        private void UpdateIcon(MatrixD matrix, Vector3D head, int id, bool show, float percent)
        {
            if(!show)
            {
                if(iconEntities[id] != null)
                {
                    iconEntities[id].Close();
                    iconEntities[id] = null;
                }
                
                if(id == Icons.WARNING)
                    warningBlinkOn = true;
                
                return;
            }
            
            if(iconEntities[id] == null)
            {
                iconEntities[id] = SpawnPrefab(settings.iconNames[id]);
                
                if(iconEntities[id] == null)
                    return;
            }
            
            if(id == Icons.WARNING && !warningBlinkOn)
            {
                matrix.Translation += (matrix.Backward * 10);
                iconEntities[id].SetWorldMatrix(matrix);
                return;
            }
            
            matrix.Translation += (matrix.Left * settings.iconLeft[id]) + (matrix.Up * settings.iconUp[id]);
            TransformHUD(ref matrix, matrix.Translation + matrix.Forward * 0.05, head, -matrix.Up, matrix.Forward);
            iconEntities[id].SetWorldMatrix(matrix);
            
            if(!settings.iconBar[id])
                return;
            
            if(iconBarEntities[id] == null)
            {
                iconBarEntities[id] = SpawnPrefab(settings.iconNames[id] + "_bar");
                
                if(iconBarEntities[id] == null)
                    return;
            }
            
            if(!warningBlinkOn && percent <= settings.iconWarnPercent[id])
            {
                matrix.Translation += (matrix.Backward * 10);
                iconBarEntities[id].SetWorldMatrix(matrix);
                return;
            }
            
            double scale = Math.Min(Math.Max((percent * HUD_BAR_MAX_SCALE) / 100, 0), HUD_BAR_MAX_SCALE);
            var align = (settings.iconFlipVertical[id] ? matrix.Left : matrix.Right);
            matrix.Translation += (align * (scale / 100.0)) - (align * (0.0097 + (0.0008 * (0.5 - (percent / 100)))));
            matrix.M11 *= scale;
            matrix.M12 *= scale;
            matrix.M13 *= scale;
            
            iconBarEntities[id].SetWorldMatrix(matrix);
        }
        
        private void TransformHUD(ref MatrixD matrix, Vector3D pos, Vector3D head, Vector3D objUp, Vector3D? objForward)
        {
            Vector3D vector, vector2, vector3;
            vector.X = pos.X - head.X;
            vector.Y = pos.Y - head.Y;
            vector.Z = pos.Z - head.Z;
            
            var num = vector.LengthSquared();
            
            if(num < 9.99999974737875E-05)
                vector = (objForward.HasValue ? (-objForward.Value) : Vector3D.Forward);
            else
                Vector3D.Multiply(ref vector, 1.0 / Math.Sqrt(num), out vector);
            
            Vector3D.Cross(ref objUp, ref vector, out vector2);
            vector2.Normalize();
            Vector3D.Cross(ref vector, ref vector2, out vector3);
            matrix.M11 = vector2.X;
            matrix.M12 = vector2.Y;
            matrix.M13 = vector2.Z;
            matrix.M14 = 0.0;
            matrix.M21 = vector3.X;
            matrix.M22 = vector3.Y;
            matrix.M23 = vector3.Z;
            matrix.M24 = 0.0;
            matrix.M31 = vector.X;
            matrix.M32 = vector.Y;
            matrix.M33 = vector.Z;
            matrix.M34 = 0.0;
            matrix.M41 = pos.X;
            matrix.M42 = pos.Y;
            matrix.M43 = pos.Z;
            matrix.M44 = 1.0;
        }
        
        private IMyEntity SpawnPrefab(string name)
        {
            var prefab = MyDefinitionManager.Static.GetPrefabDefinition(name);
            
            if(prefab == null)
            {
                Log.Error("Can't find prefab: " + name);
                return null;
            }
            
            if(prefab.CubeGrids == null)
            {
                MyDefinitionManager.Static.ReloadPrefabsFromFile(prefab.PrefabPath);
                prefab = MyDefinitionManager.Static.GetPrefabDefinition(name);
            }
            
            MyObjectBuilder_CubeGrid builder = prefab.CubeGrids[0].Clone() as MyObjectBuilder_CubeGrid;
            builder.PersistentFlags = MyPersistentEntityFlags2.InScene;
            builder.Name = "";
            builder.DisplayName = "";
            builder.CreatePhysics = false;
            builder.PositionAndOrientation = new MyPositionAndOrientation(Vector3D.Zero, Vector3D.Forward, Vector3D.Up);
            
            MyAPIGateway.Entities.RemapObjectBuilder(builder);
            var ent = MyAPIGateway.Entities.CreateFromObjectBuilder(builder);
            ent.Flags &= ~EntityFlags.Sync; // don't sync on MP
            ent.Flags &= ~EntityFlags.Save; // don't save this entity
            
            MyAPIGateway.Entities.AddEntity(ent, true);
            
            ent.Render.CastShadows = false;
            
            return ent;
        }
        
        public void MessageEntered(string msg, ref bool visible)
        {
            if(!msg.StartsWith("/helmet", StringComparison.InvariantCultureIgnoreCase))
                return;
            
            if(settings.reminder)
            {
                settings.reminder = false;
                settings.Save();
            }
            
            visible = false;
            msg = msg.Substring("/helmet".Length).Trim().ToLower();
            
            if(msg.Length > 0)
            {
                if(msg.StartsWith("for fov"))
                {
                    msg = msg.Substring("for fov".Length).Trim();
                    
                    float fov = 0;
                    
                    if(msg.Length == 0)
                    {
                        fov = MathHelper.ToDegrees(MyAPIGateway.Session.Config.FieldOfView);
                        MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Your FOV is: " + fov);
                    }
                    else if(!float.TryParse(msg, out fov))
                    {
                        MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Invalid float number: " + msg);
                    }
                    
                    if(fov > 0)
                    {
                        settings.ScaleForFOV(fov);
                        settings.Save();
                        MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "HUD and helmet scale set to "+settings.scale+"; saved to config.");
                    }
                    
                    return;
                }
                else if(msg.StartsWith("scale"))
                {
                    msg = msg.Substring("scale".Length).Trim();
                    
                    if(msg.Length == 0)
                    {
                        MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Scale = "+settings.scale);
                        return;
                    }
                    
                    double scale;
                    
                    if(double.TryParse(msg, out scale))
                    {
                        scale = Math.Min(Math.Max(scale, Settings.MIN_SCALE), Settings.MAX_SCALE);
                        settings.scale = scale;
                        settings.Save();
                        MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Scale set to "+scale+"; saved to config.");
                    }
                    else
                    {
                        MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Invalid float number: " + msg);
                    }
                    
                    return;
                }
                else if(msg.StartsWith("hud scale"))
                {
                    msg = msg.Substring("hud scale".Length).Trim();
                    
                    if(msg.Length == 0)
                    {
                        MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "HUD scale = "+settings.scale);
                        return;
                    }
                    
                    double scale;
                    
                    if(double.TryParse(msg, out scale))
                    {
                        scale = Math.Min(Math.Max(scale, Settings.MIN_HUDSCALE), Settings.MAX_HUDSCALE);
                        settings.hudScale = scale;
                        settings.Save();
                        MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "HUD scale set to "+scale+"; saved to config.");
                    }
                    else
                    {
                        MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Invalid float number: " + msg);
                    }
                    
                    return;
                }
                else if(msg.StartsWith("off"))
                {
                    MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Turned OFF; saved to config.");
                    settings.enabled = false;
                    settings.Save();
                    return;
                }
                else if(msg.StartsWith("on"))
                {
                    MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Turned ON; saved to config.");
                    settings.enabled = true;
                    settings.Save();
                    return;
                }
                else if(msg.StartsWith("hud off"))
                {
                    MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "HUD turned OFF; saved to config.");
                    settings.hud = false;
                    settings.Save();
                    return;
                }
                else if(msg.StartsWith("hud on"))
                {
                    MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "HUD turned ON; saved to config.");
                    settings.hud = true;
                    settings.Save();
                    return;
                }
                else if(msg.StartsWith("reload"))
                {
                    settings.Load();
                    MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Reloaded settings from the config file.");
                    MyAPIGateway.Utilities.ShowMessage("Config path", "%appdata%\\SpaceEngineers\\Storage\\428842256_Helmet\\helmet.cfg");
                    return;
                }
            }
            
            MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Available commands:");
            MyAPIGateway.Utilities.ShowMessage("/helmet for fov [number] ", "number is optional, quickly set scales for a specified FOV value");
            MyAPIGateway.Utilities.ShowMessage("/helmet <on/off> ", "turn the entire mod on or off");
            MyAPIGateway.Utilities.ShowMessage("/helmet scale <number> ", "-1.0 to 1.0, default 0");
            MyAPIGateway.Utilities.ShowMessage("/helmet hud <on/off> ", "turn the HUD component on or off");
            MyAPIGateway.Utilities.ShowMessage("/helmet hud scale <number> ", "-1.0 to 1.0, default 0");
            MyAPIGateway.Utilities.ShowMessage("/helmet reload ", "re-loads the config file (for advanced editing)");
        }
    }
}