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
using Sandbox.Common.ObjectBuilders.VRageData;
using Sandbox.Definitions;
using Sandbox.Engine;
using Sandbox.Engine.Physics;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRageMath;
using VRage;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Components;
using VRage.Utils;
using Digi.Utils;
using Digi.Helmet;
using IMyCockpit = Sandbox.ModAPI.Ingame.IMyCockpit;
using IMyShipController = Sandbox.ModAPI.Ingame.IMyShipController;
using IMyDestroyableObject = Sandbox.ModAPI.Interfaces.IMyDestroyableObject;
using IMyGravityGenerator = Sandbox.ModAPI.Ingame.IMyGravityGenerator;
using IMyGravityGeneratorBase = Sandbox.ModAPI.Ingame.IMyGravityGeneratorBase;
using IMyGravityGeneratorSphere = Sandbox.ModAPI.Ingame.IMyGravityGeneratorSphere;
using Ingame = Sandbox.ModAPI.Ingame;

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
        private long lastReminder;
        private bool warningBlinkOn = true;
        private double lastWarningBlink = 0;
        private long helmetBroken = 0;
        
        private float oldFov = 60;
        private int slowUpdateFov = 0;
        
        public static List<IMyGravityGeneratorBase> gravityGenerators = new List<IMyGravityGeneratorBase>();
        private Vector3 gravityDir = Vector3.Zero;
        private float gravityForce = 0;
        private int gravitySources = 0;
        private bool prevGravityStatus = false;
        private int slowUpdateGravDir = 0;
        
        private bool firstHelmetSpawn = true;
        private MatrixD helmetMatrix;
        private float[] values = new float[Settings.TOTAL_ELEMENTS];
        private bool[] show = new bool[Settings.TOTAL_ELEMENTS];
        private MatrixD lastMatrix;
        //private MatrixD temp;
        //private Vector3D pos;
        private int lastOxygenEnv = 0;
        private bool fatalError = false;
        
        //private MyObjectBuilder_Character characterObject;
        private int slowUpdateCharObj = 0;
        
        public static IMyEntity holdingTool = null;
        public static string holdingToolTypeId = null;
        public static string holdingToolSubtypeId = null;
        
        public IMyEntity[] iconEntities = new IMyEntity[Settings.TOTAL_ELEMENTS];
        public IMyEntity[] iconBarEntities = new IMyEntity[Settings.TOTAL_ELEMENTS];
        
        double lastLinearSpeed;
        Vector3D lastDirSpeed;
        
        private int skipDisplay = 0;
        private float prevSpeed = 0;
        
        /*
        private float prevBattery = -1;
        private long prevBatteryTime = 0;
        private float prevO2 = -1;
        private long prevO2Time = 0;
        private float prevH = -1;
        private long prevHTime = 0;
        private float etaPower = 0;
        private float etaO2 = 0;
        private float etaH = 0;
         */
        
        private StringBuilder str = new StringBuilder();
        private const string DISPLAY_PAD = " ";
        private const float DISPLAY_FONT_SIZE = 1.3f;
        
        //private int flickerResetBgColor = 0;
        //private int flickerTimeOut = 0;
        
        private Random rand = new Random();
        private Dictionary<string, int> components = new Dictionary<string, int>();
        private List<IMySlimBlock> blocks = new List<IMySlimBlock>();
        private StringBuilder tmp = new StringBuilder();
        private string lastDisplayText = null;
        
        private const int HUD_BAR_MAX_SCALE = 376;
        private const float SCALE_DIST_ADJUST = 0.1f;
        private const float HUDSCALE_DIST_ADJUST = 0.1f;
        
        private const string CUBE_HELMET_PREFIX = "Helmet_";
        private const string CUBE_HELMET_BROKEN_SUFFIX = "vignetteBroken";
        private const string CUBE_HUD_PREFIX = "HelmetHUD_";
        private const string MOD_NAME = "Helmet";
        
        public static readonly MyObjectBuilder_AmmoMagazine AMMO_BULLETS = new MyObjectBuilder_AmmoMagazine() { SubtypeName = "NATO_25x184mm" };
        public static readonly MyObjectBuilder_AmmoMagazine AMMO_MISSILES = new MyObjectBuilder_AmmoMagazine() { SubtypeName = "Missile200mm" };
        
        private const string HELP_COMMANDS =
            "/helmet for fov [number]   number is optional, quickly set scales for a specified FOV value\n" +
            "/helmet <on/off>   turn the entire mod on or off\n" +
            "/helmet scale <number>   -1.0 to 1.0, default 0\n" +
            "/helmet hud <on/off>   turn the HUD component on or off\n" +
            "/helmet hud scale <number>   -1.0 to 1.0, default 0\n" +
            "/helmet reload   re-loads the config file (for advanced editing)\n" +
            "/helmet <dx9/dx11>   change models for DX9 or DX11\n" +
            "/helmet glass <on/off>   turn glass reflections on or off\n" +
            "\n" +
            "For advanced editing go to:\n" +
            "%appdata%\\SpaceEngineers\\Storage\\428842256_Helmet\\helmet.cfg";
        
        public void Init()
        {
            Log.Info("Initialized");
            
            init = true;
            isServer = MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE || MyAPIGateway.Multiplayer.IsServer;
            isDedicated = (MyAPIGateway.Utilities.IsDedicated && isServer);
            
            if(isDedicated)
                return;
            
            settings = new Settings();
            MyAPIGateway.Utilities.MessageEntered += MessageEntered;
            MyAPIGateway.Session.DamageSystem.RegisterDestroyHandler(999, EntityKilled);
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
        
        public void EntityKilled(object obj, MyDamageInformation info)
        {
            if(characterEntity != null && obj is IMyCharacter)
            {
                var ent = obj as IMyEntity;
                
                if(ent.EntityId == characterEntity.EntityId)
                {
                    //MyAPIGateway.Utilities.ShowMessage("debug :", "killed by "+info.Type.String+"; damage="+info.Amount);
                    
                    switch(info.Type.String)
                    {
                        case "Environment":
                        case "Explosion":
                        case "Bullet":
                            helmetBroken = ent.EntityId;
                            break;
                    }
                }
            }
        }
        
        //private Benchmark bench_update = new Benchmark("update");
        
        public override void UpdateAfterSimulation()
        {
            //bench_update.Start();
            Update();
            //bench_update.End();
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
            
            if(characterEntity != null)
            {
                if(characterEntity.MarkedForClose || characterEntity.Closed)
                {
                    characterEntity = null;
                }
                else if(settings.autoFovScale)
                {
                    if(++slowUpdateFov >= 60)
                    {
                        slowUpdateFov = 0;
                        float fov = MathHelper.ToDegrees(MyAPIGateway.Session.Config.FieldOfView);
                        
                        if(oldFov != fov)
                        {
                            settings.ScaleForFOV(fov);
                            settings.Save();
                            oldFov = fov;
                        }
                    }
                }
            }
            
            var camera = MyAPIGateway.Session.CameraController;
            
            if(camera != null && camera.IsInFirstPersonView && MyAPIGateway.Session.ControlledObject != null && MyAPIGateway.Session.ControlledObject.Entity != null)
            {
                var contrEnt = MyAPIGateway.Session.ControlledObject.Entity;
                
                if(camera is IMyCharacter || (camera == contrEnt && contrEnt is IMyCockpit))
                {
                    if(camera is IMyCharacter)
                    {
                        characterEntity = camera as IMyEntity;
                    }
                    else if(contrEnt is IMyCharacter)
                    {
                        characterEntity = contrEnt;
                    }
                    else if(contrEnt is IMyCockpit)
                    {
                        if(characterEntity == null && contrEnt.Hierarchy.Children.Count > 0)
                        {
                            foreach(var child in contrEnt.Hierarchy.Children)
                            {
                                if(child.Entity is IMyCharacter && child.Entity.DisplayName == MyAPIGateway.Session.Player.DisplayName)
                                {
                                    characterEntity = child.Entity;
                                    break;
                                }
                            }
                        }
                    }
                    
                    if(AttachHelmet())
                    {
                        return;
                    }
                }
            }
            
            RemoveHelmet();
        }
        
        private bool AttachHelmet()
        {
            var controllable = characterEntity as Sandbox.ModAPI.Interfaces.IMyControllableEntity;
            
            if(controllable != null && controllable.EnabledHelmet)
            {
                // ignoring head Y axis on foot because of an issue with ALT and 3rd person camera bumping into the ground
                bool inCockpit = (MyAPIGateway.Session.ControlledObject.Entity is IMyCockpit);
                UpdateHelmetAt(MyAPIGateway.Session.ControlledObject.GetHeadMatrix(inCockpit, true));
                return true;
            }
            
            return false;
        }
        
        private void RemoveHelmet(bool removeHud = true)
        {
            if(removedHelmet)
                return;
            
            removedHelmet = true;
            
            if(helmet != null)
            {
                if(characterEntity != null)
                    helmet.SetPosition(characterEntity.WorldMatrix.Translation + characterEntity.WorldMatrix.Backward * 5000);
                else
                    helmet.SetPosition(Vector3D.Zero);
                
                helmet.Close();
                helmet = null;
            }
            
            if(removeHud)
                RemoveHud();
        }
        
        private void RemoveHud()
        {
            if(removedHUD)
                return;
            
            removedHUD = true;
            
            for(int id = 0; id < Settings.TOTAL_ELEMENTS; id++)
            {
                if(iconEntities[id] != null)
                {
                    if(characterEntity != null)
                        iconEntities[id].SetPosition(characterEntity.WorldMatrix.Translation + characterEntity.WorldMatrix.Backward * 5000);
                    else
                        iconEntities[id].SetPosition(Vector3D.Zero);
                    
                    iconEntities[id].Close();
                    iconEntities[id] = null;
                }
                
                if(iconBarEntities[id] != null)
                {
                    if(characterEntity != null)
                        iconBarEntities[id].SetPosition(characterEntity.WorldMatrix.Translation + characterEntity.WorldMatrix.Backward * 5000);
                    else
                        iconBarEntities[id].SetPosition(Vector3D.Zero);
                    
                    iconBarEntities[id].Close();
                    iconBarEntities[id] = null;
                }
            }
            
            lastDisplayText = null;
        }
        
        private void UpdateHelmetAt(MatrixD matrix)
        {
            try
            {
                if(fatalError)
                    return; // stop trying if a fatal error occurs
                
                /*
                bool slowUpdate = false; // slow update stats later on
                
                // Update the character object slowly
                if(++slowUpdateCharObj % 10 == 0)
                {
                    slowUpdateCharObj = 0;
                    slowUpdate = true;
                    //characterObject = characterEntity.GetObjectBuilder(false) as MyObjectBuilder_Character;
                }
                 */
                
                if(firstHelmetSpawn)
                {
                    firstHelmetSpawn = false;
                    
                    if(settings.hydrogenUpdateReset)
                        MyAPIGateway.Utilities.ShowMissionScreen("Helmet mod update", "", "TL;DR: Running DX11 ? Type in chat: /helmet dx11", "\nThe mod now has individual tweaks for DX9 and DX11 and because it can't detect what you're running it's set for DX9 by default.\n\nIf you're running DX11 type /helmet dx11 in chat!\n\nAlso there's a new LCD screen if you hide the vanilla HUD!\n\n\n\nFor the full changelog visit the workshop page: http://steamcommunity.com/sharedfiles/filedetails/?id=428842256 (or just search for 'helmet' in the Space Engineers Steam workshop).", null, "Close");
                }
                
                bool brokenHelmet = helmetBroken > 0 && helmetBroken == characterEntity.EntityId;
                
                if(brokenHelmet)
                {
                    RemoveHelmet(false);
                    helmetBroken = 0;
                }
                
                // Spawn the helmet model if it's not spawned
                if(brokenHelmet || (helmet == null && settings.helmetModel != null))
                {
                    helmet = SpawnPrefab(settings.GetRenderPrefix() + CUBE_HELMET_PREFIX + (brokenHelmet ? CUBE_HELMET_BROKEN_SUFFIX : settings.GetHelmetModel()));
                    
                    if(helmet == null)
                    {
                        Log.Error("Couldn't load the helmet prefab!");
                        fatalError = true;
                        return;
                    }
                }
                
                // No smoothing when in a cockpit/seat since it already is smoothed
                if(!(MyAPIGateway.Session.Player.Controller.ControlledEntity.Entity is Sandbox.ModAPI.Ingame.IMyCockpit))
                {
                    if(settings.delayedRotation > 0)
                    {
                        /*
                        pos = matrix.Translation;
                        temp = MatrixD.Lerp(lastMatrix, matrix, (1.0f - settings.delayedRotation));
                        lastMatrix = matrix;
                        matrix = temp;
                        matrix.Translation = pos;
                         */
                        
                        double lenDiff = (lastMatrix.Forward - matrix.Forward).Length() * 4 * settings.delayedRotation;
                        double amount = MathHelper.Clamp(lenDiff, 0.25, 1.0);
                        
                        double linearSpeed = (lastMatrix.Translation - matrix.Translation).Length();
                        double linearAccel = lastLinearSpeed - linearSpeed;
                        
                        Vector3D dirSpeed = (lastMatrix.Translation - matrix.Translation);
                        Vector3D dirAccel = (lastDirSpeed - dirSpeed);
                        
                        double accel = dirAccel.Length();
                        
                        lastDirSpeed = dirSpeed;
                        lastLinearSpeed = linearSpeed;
                        
                        double minMax = 0.005;
                        double posAmount = MathHelper.Clamp(accel / 10, 0.01, 1.0);
                        
                        matrix.M11 = lastMatrix.M11 + (matrix.M11 - lastMatrix.M11) * amount;
                        matrix.M12 = lastMatrix.M12 + (matrix.M12 - lastMatrix.M12) * amount;
                        matrix.M13 = lastMatrix.M13 + (matrix.M13 - lastMatrix.M13) * amount;
                        matrix.M14 = lastMatrix.M14 + (matrix.M14 - lastMatrix.M14) * amount;
                        matrix.M21 = lastMatrix.M21 + (matrix.M21 - lastMatrix.M21) * amount;
                        matrix.M22 = lastMatrix.M22 + (matrix.M22 - lastMatrix.M22) * amount;
                        matrix.M23 = lastMatrix.M23 + (matrix.M23 - lastMatrix.M23) * amount;
                        matrix.M24 = lastMatrix.M24 + (matrix.M24 - lastMatrix.M24) * amount;
                        matrix.M31 = lastMatrix.M31 + (matrix.M31 - lastMatrix.M31) * amount;
                        matrix.M32 = lastMatrix.M32 + (matrix.M32 - lastMatrix.M32) * amount;
                        matrix.M33 = lastMatrix.M33 + (matrix.M33 - lastMatrix.M33) * amount;
                        matrix.M34 = lastMatrix.M34 + (matrix.M34 - lastMatrix.M34) * amount;
                        
                        matrix.M41 = matrix.M41 + MathHelper.Clamp((lastMatrix.M41 - matrix.M41) * posAmount, -minMax, minMax);
                        matrix.M42 = matrix.M42 + MathHelper.Clamp((lastMatrix.M42 - matrix.M42) * posAmount, -minMax, minMax);
                        matrix.M43 = matrix.M43 + MathHelper.Clamp((lastMatrix.M43 - matrix.M43) * posAmount, -minMax, minMax);
                        
                        matrix.M44 = lastMatrix.M44 + (matrix.M44 - lastMatrix.M44) * amount;
                        
                        lastMatrix = matrix;
                    }
                }
                
                // Align the helmet mesh
                helmetMatrix = matrix;
                helmetMatrix.Translation += matrix.Forward * (SCALE_DIST_ADJUST * (1.0 - settings.scale));
                
                if(helmet != null)
                    helmet.SetWorldMatrix(helmetMatrix);
                
                removedHelmet = false;
                
                // if HUD is disabled or we can't get the info, remove the HUD (if exists) and stop here
                if(!settings.hud) // || characterObject == null)
                {
                    RemoveHud();
                    return;
                }
                
                var c = MyHud.CharacterInfo;
                
                if(++slowUpdateCharObj % 5 == 0)
                {
                    slowUpdateCharObj = 0;
                    
                    // show and value cache for the HUD elements
                    for(int id = 0; id < Settings.TOTAL_ELEMENTS; id++)
                    {
                        values[id] = 0;
                        show[id] = settings.elements[id].show;
                    }
                    
                    bool inShip = MyHud.ShipInfo.Visible;
                    var v = MyHud.ShipInfo;
                    
                    show[Icons.BROADCASTING] = MyHud.CharacterInfo.BroadcastEnabled; // characterObject.EnableBroadcasting;
                    show[Icons.DAMPENERS] = (inShip ? MyHud.ShipInfo.DampenersEnabled : c.DampenersEnabled); // characterObject.DampenersEnabled;
                    
                    values[Icons.HEALTH] = (characterEntity is IMyDestroyableObject ? (characterEntity as IMyDestroyableObject).Integrity : 100);
                    values[Icons.ENERGY] = c.BatteryEnergy; // (characterObject.Battery != null ? characterObject.Battery.CurrentCapacity * 10000000 : 0);
                    values[Icons.HYDROGEN] = c.HydrogenRatio * 100;
                    values[Icons.OXYGEN] = c.OxygenLevel * 100; // characterObject.OxygenLevel * 100;
                    values[Icons.OXYGEN_ENV] = (characterEntity as IMyCharacter).EnvironmentOxygenLevel * 2;
                    values[Icons.INVENTORY] = 0;
                    
                    // Get the inventory volume
                    MyInventory inv;
                    
                    if((characterEntity as MyEntity).TryGetInventory(out inv))
                        values[Icons.INVENTORY] = ((float)inv.CurrentVolume / (float)inv.MaxVolume) * 100;
                }
                
                // Update the warning icon
                show[Icons.WARNING] = false;
                
                int moveMode = (c.JetpackEnabled ? 2 : 1);
                
                if(settings.elements[Icons.WARNING].show)
                {
                    for(int id = 0; id < Settings.TOTAL_ELEMENTS; id++)
                    {
                        if(settings.elements[id].warnPercent >= 0 && values[id] <= settings.elements[id].warnPercent)
                        {
                            if(settings.elements[id].warnMoveMode == 0 || settings.elements[id].warnMoveMode == moveMode)
                            {
                                double warnTick = DateTime.UtcNow.Ticks;
                                
                                if(lastWarningBlink < warnTick)
                                {
                                    warningBlinkOn = !warningBlinkOn;
                                    lastWarningBlink = warnTick + (TimeSpan.TicksPerSecond * settings.warnBlinkTime);
                                }
                                
                                show[Icons.WARNING] = true;
                                break;
                            }
                        }
                    }
                }
                
                if(show[Icons.GRAVITY] || show[Icons.DISPLAY])
                {
                    if(++slowUpdateGravDir % 6 == 0)
                    {
                        slowUpdateGravDir = 0;
                        GetGravityAtPoint(characterEntity.WorldAABB.Center, ref gravityDir, ref gravityForce, ref gravitySources);
                        
                        bool inGravity = gravityForce > 0;
                        
                        if(inGravity)
                        {
                            float div = 1f / gravityForce;
                            gravityDir.X *= div;
                            gravityDir.Y *= div;
                            gravityDir.Z *= div;
                        }
                        
                        if(prevGravityStatus != inGravity)
                        {
                            if(iconEntities[Icons.GRAVITY] != null)
                            {
                                iconEntities[Icons.GRAVITY].Close(); // remove the gravity icon so it can be re-added as the changed one
                                iconEntities[Icons.GRAVITY] = null;
                            }
                            
                            prevGravityStatus = !prevGravityStatus;
                        }
                    }
                }
                
                /*
                if(settings.elements[Icons.WARNING].show &&
                   (values[Icons.HEALTH] <= settings.elements[Icons.HEALTH].warnPercent
                    || values[Icons.ENERGY] <= settings.elements[Icons.ENERGY].warnPercent
                    || values[Icons.OXYGEN] <= settings.elements[Icons.OXYGEN].warnPercent
                    || (values[Icons.HYDROGEN] <= settings.elements[Icons.HYDROGEN].warnPercent && (settings.elements[Icons.HYDROGEN].warnMoveMode == 0 ? true : settings.elements[Icons.HYDROGEN].warnMoveMode == moveMode))))
                {
                    double warnTick = DateTime.UtcNow.Ticks;
                    
                    if(lastWarningBlink < warnTick)
                    {
                        warningBlinkOn = !warningBlinkOn;
                        lastWarningBlink = warnTick + (TimeSpan.TicksPerSecond * settings.warnBlinkTime);
                    }
                    
                    show[Icons.WARNING] = true;
                }
                 */
                
                matrix.Translation += matrix.Forward * (HUDSCALE_DIST_ADJUST * (1.0 - settings.hudScale)); // off-set the HUD according to the HUD scale
                var headPos = matrix.Translation + (matrix.Backward * 0.25); // for glass curve effect
                bool hudVisible = !MyAPIGateway.Session.Config.MinimalHud; // if the vanilla HUD is on or off
                removedHUD = false; // mark the HUD as not removed because we're about to spawn it
                
                // spawn and update the HUD elements
                for(int id = 0; id < Settings.TOTAL_ELEMENTS; id++)
                {
                    bool showIcon = show[id];
                    
                    if(showIcon)
                    {
                        switch(settings.elements[id].hudMode)
                        {
                            case 1:
                                showIcon = hudVisible;
                                break;
                            case 2:
                                showIcon = !hudVisible;
                                break;
                        }
                    }
                    
                    UpdateIcon(matrix, headPos, id, showIcon, values[id]);
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        private void UpdateIcon(MatrixD matrix, Vector3D headPos, int id, bool show, float percent)
        {
            try
            {
                HudElement element = settings.elements[id];
                
                // if the element should be hidden, take action!
                if(!show)
                {
                    // remove the element itself if it exists
                    if(iconEntities[id] != null)
                    {
                        iconEntities[id].Close();
                        iconEntities[id] = null;
                    }
                    
                    if(id == Icons.WARNING)
                        warningBlinkOn = true; // reset the blink status for the warning icon
                    
                    // remove the bar if it has one and it exists
                    if(element.hasBar && iconBarEntities[id] != null)
                    {
                        iconBarEntities[id].Close();
                        iconBarEntities[id] = null;
                    }
                    
                    return; // and STOP!
                }
                
                string name = element.name;
                
                // append the oxygen level number to the name and remove the previous entity if changed
                if(id == Icons.OXYGEN_ENV)
                {
                    int oxygenEnv = Math.Min(Math.Max((int)Math.Round(percent), 0), 2);
                    
                    if(iconEntities[id] != null && lastOxygenEnv != oxygenEnv)
                    {
                        iconEntities[id].Close();
                        iconEntities[id] = null;
                    }
                    
                    lastOxygenEnv = oxygenEnv;
                    name += oxygenEnv.ToString();
                }
                
                // set the gravity icon type and update the gravity direction
                if(id == Icons.GRAVITY)
                {
                    if(gravityDir == Vector3.Zero)
                        name += "None";
                    else
                        name += "Dir";
                }
                
                // spawn the element if it's not already
                if(iconEntities[id] == null)
                {
                    if(id == Icons.DISPLAY)
                    {
                        iconEntities[id] = SpawnPrefab(CUBE_HUD_PREFIX + name + (settings.displayQuality == 0 ? "Low" : ""), true);
                        lastDisplayText = null; // force first write
                    }
                    else
                        iconEntities[id] = SpawnPrefab(settings.GetRenderPrefix() + CUBE_HUD_PREFIX + name);
                    
                    if(iconEntities[id] == null)
                        return;
                }
                
                // blink the warning icon by moving it out of view and back in view
                if(id == Icons.WARNING && !warningBlinkOn)
                {
                    matrix.Translation += (matrix.Backward * 10);
                    iconEntities[id].SetWorldMatrix(matrix);
                    return;
                }
                
                matrix.Translation += (matrix.Left * element.posLeft) + (matrix.Up * element.posUp);
                
                // update the gravity indicator
                if(id == Icons.GRAVITY && gravityDir != Vector3.Zero)
                {
                    matrix.Translation += matrix.Forward * 0.05;
                    
                    AlignToVector(ref matrix, gravityDir);
                    
                    // TODO fix the perspective misalignment
                    
                    iconEntities[id].SetWorldMatrix(matrix);
                    return;
                }
                
                if(id == Icons.DISPLAY)
                    headPos += matrix.Forward * 0.23; // more curvature for the screen
                
                // align the element to the view and give it the glass curve
                TransformHUD(ref matrix, matrix.Translation + matrix.Forward * 0.05, headPos, -matrix.Up, matrix.Forward);
                iconEntities[id].SetWorldMatrix(matrix);
                
                // update the display
                if(id == Icons.DISPLAY)
                {
                    if(++skipDisplay % (60 / settings.displayUpdateRate) == 0)
                    {
                        skipDisplay = 0;
                        var ghostGrid = iconEntities[id] as IMyCubeGrid;
                        var lcdSlim = ghostGrid.GetCubeBlock(Vector3I.Zero);
                        
                        if(lcdSlim == null)
                        {
                            Log.Error("Can't find LCD in the grid!");
                            return;
                        }
                        
                        var lcd = lcdSlim.FatBlock as Ingame.IMyTextPanel;
                        
                        if(settings.displayBorderSuitColor && slowUpdateCharObj == 0)
                        {
                            var charObj = characterEntity.GetObjectBuilder(false) as MyObjectBuilder_Character;
                            var charColor = charObj.ColorMaskHSV;
                            
                            if(Vector3.DistanceSquared(lcdSlim.GetColorMask(), charColor) > 0f)
                            {
                                ghostGrid.ColorBlocks(Vector3I.Zero, Vector3I.Zero, charColor);
                                lastDisplayText = null; // force rewrite
                            }
                        }
                        
                        if(MyHud.CharacterInfo.HealthRatio <= 0)
                        {
                            str.Append(DISPLAY_PAD).Append("ERROR #404:").AppendLine();
                            str.Append(DISPLAY_PAD).Append("USER LIFESIGNS NOT FOUND.").AppendLine();
                        }
                        else try
                        {
                            bool inShip = MyHud.ShipInfo.Visible;
                            float speed = 0; // (float)Math.Round((inShip ? MyHud.ShipInfo.Speed : MyHud.CharacterInfo.Speed), 2);
                            float accel = 0;
                            char accelSymbol = '-';
                            int battery = (int)MyHud.CharacterInfo.BatteryEnergy;
                            
                            if(inShip)
                            {
                                var block = MyAPIGateway.Session.ControlledObject.Entity as IMyCubeBlock;
                                
                                if(block != null && block.CubeGrid != null && block.CubeGrid.Physics != null)
                                {
                                    speed = (float)Math.Round(block.CubeGrid.Physics.LinearVelocity.Length(), 2);
                                    
                                    if(speed > 0)
                                        accel = (float)Math.Round(block.CubeGrid.Physics.LinearAcceleration.Length(), 2);
                                    else
                                        accel = 0;
                                }
                            }
                            else
                            {
                                var player = MyAPIGateway.Session.ControlledObject.Entity;
                                
                                if(player != null && player.Physics != null)
                                {
                                    speed = (float)Math.Round(player.Physics.LinearVelocity.Length(), 2);
                                    
                                    if(speed > 0)
                                        accel = (float)Math.Round(player.Physics.LinearAcceleration.Length(), 2);
                                    else
                                        accel = 0;
                                }
                            }
                            
                            if(speed >= prevSpeed)
                                accelSymbol = '+';
                            
                            prevSpeed = speed;
                            
                            str.Clear();
                            
                            string unit = "m/s";
                            
                            if(settings.displaySpeedUnit == SpeedUnits.kph)
                            {
                                speed *= 3.6f;
                                unit = "km/h";
                            }
                            
                            str.Append(DISPLAY_PAD).Append("Speed: ").Append(speed).Append(unit).Append(" (").Append(accelSymbol).Append(Math.Round(accel, 2)).Append(unit).Append(")").AppendLine();
                            
                            str.Append(DISPLAY_PAD).Append("Gravity: ");
                            if(gravitySources > 0)
                                str.Append(Math.Round(gravityForce, 2)).Append("g (").Append(gravitySources).Append(gravitySources > 1 ? " fields" : " field").Append(")");
                            else
                                str.Append("Not in gravity");
                            str.AppendLine();
                            
                            if(inShip)
                            {
                                var s = MyHud.ShipInfo;
                                str.Append(DISPLAY_PAD).Append("Ship mass: ");
                                
                                if(s.Mass > 1000000)
                                    str.Append(s.Mass / 100000).Append(" tonnes");
                                else
                                    str.Append(s.Mass).Append(" kg");
                                
                                str.AppendLine();
                                
                                str.Append(DISPLAY_PAD).Append("Power: ");
                                
                                if(s.ResourceState == MyResourceStateEnum.NoPower)
                                {
                                    str.Append("No power");
                                }
                                else
                                {
                                    if(s.ResourceState == MyResourceStateEnum.OverloadBlackout)
                                        str.Append("Overload");
                                    else
                                        str.Append(Math.Floor(s.PowerUsage * 100)).Append("%");
                                    
                                    str.Append(" (");
                                    MyValueFormatter.AppendTimeInBestUnit(s.FuelRemainingTime * 3600f, str);
                                    str.Append(")");
                                }
                                
                                str.AppendLine();
                                str.Append(DISPLAY_PAD).Append("Landing gears: ").Append(s.LandingGearsInProximity).Append(" / ").Append(s.LandingGearsLocked).Append(" / ").Append(s.LandingGearsTotal).AppendLine();
                                str.AppendLine();
                            }
                            else
                            {
                                str.Append(DISPLAY_PAD).Append("Mass: ").Append(MyHud.CharacterInfo.Mass.ToString("N")).Append(" kg").AppendLine();
                                str.Append(DISPLAY_PAD).Append("Inventory: ").Append(Math.Round((float)MyHud.CharacterInfo.InventoryVolume * 1000, 2)).Append(" L").AppendLine();
                                
                                /*
                                float power = MyHud.CharacterInfo.BatteryEnergy;
                                float o2 = MyHud.CharacterInfo.OxygenLevel * 100;
                                float h = MyHud.CharacterInfo.HydrogenRatio * 100;
                                long now = DateTime.UtcNow.Ticks;
                                
                                if(prevBattery != power)
                                {
                                    if(prevBattery > power)
                                    {
                                        float elapsed = (float)TimeSpan.FromTicks(now - prevBatteryTime).TotalSeconds;
                                        etaPower = (0 - power) / ((power - prevBattery) / elapsed);
                                        
                                        if(etaPower < 0)
                                            etaPower = float.PositiveInfinity;
                                        
                                        prevBatteryTime = now;
                                    }
                                    else
                                        etaPower = float.PositiveInfinity;
                                    
                                    prevBattery = power;
                                }
                                
                                if(prevO2 != o2)
                                {
                                    float elapsed = (float)TimeSpan.FromTicks(now - prevO2Time).TotalSeconds;
                                    etaO2 = (0 - o2) / ((o2 - prevO2) / elapsed);
                                    
                                    if(etaO2 < 0)
                                        etaO2 = float.PositiveInfinity;
                                    
                                    prevO2 = o2;
                                    prevO2Time = now;
                                }
                                
                                float elapsedH = (float)TimeSpan.FromTicks(now - prevHTime).TotalSeconds;
                                
                                if(prevH != h || elapsedH > (TimeSpan.TicksPerSecond * 2))
                                {
                                    float diff = h - prevH;
                                    
                                    if(diff < 0)
                                    {
                                        etaH = (0 - h) / ((h - prevH) / elapsedH);
                                        
                                        if(etaH < 0)
                                            etaH = float.PositiveInfinity;
                                    }
                                    else
                                        etaH = float.PositiveInfinity;
                                    
                                    prevH = h;
                                    prevHTime = now;
                                }
                                
                                // TODO >...............
                                
                                str.Append(LCD_PAD);
                                
                                if(etaPower < etaO2 && etaPower < etaH)
                                {
                                    str.Append("Battery: ").Append(Math.Round(power, 2)).Append("% (");
                                    MyValueFormatter.AppendTimeInBestUnit(etaPower, str);
                                    str.Append(")");
                                }
                                else if(etaO2 < etaPower && etaO2 < etaH)
                                {
                                    str.Append("Oxygen: ").Append(Math.Round(o2, 2)).Append("% (");
                                    MyValueFormatter.AppendTimeInBestUnit(etaO2, str);
                                    str.Append(")");
                                }
                                else if(etaH < etaPower && etaH < etaO2)
                                {
                                    str.Append("Hydrogen: ").Append(Math.Round(h, 2)).Append("% (");
                                    MyValueFormatter.AppendTimeInBestUnit(etaH, str);
                                    str.Append(")");
                                }
                                else
                                {
                                    str.Append("Calculating...");
                                }
                                 */
                                
                                str.AppendLine();
                                
                                /*
                                str.Append(LCD_PAD).Append("Battery: ");
                                
                                float bat = MyHud.CharacterInfo.BatteryEnergy;
                                
                                if(bat != prevBattery)
                                {
                                    long now = DateTime.UtcNow.Ticks;
                                    float elapsed = (float)TimeSpan.FromTicks(now - prevBatteryTime).TotalSeconds;
                                    float diff = bat - prevBattery;
                                    float eta;
                                    tmp.Clear();
                                    
                                    if(prevBattery > bat)
                                    {
                                        eta = (0 - bat) / (diff / elapsed);
                                    }
                                    else
                                    {
                                        tmp.Append("+");
                                        eta = (100 - bat) / (diff / elapsed);
                                    }
                                    
                                    tmp.Append(Math.Round(bat, 2)).Append("% (");
                                    MyValueFormatter.AppendTimeInBestUnit(eta, tmp);
                                    tmp.Append(")");
                                    batteryTimeCache = tmp.ToString();
                                    tmp.Clear();
                                    
                                    prevBatteryTime = now;
                                    prevBattery = bat;
                                }
                                
                                if(batteryTimeCache == null)
                                    str.Append(Math.Round(bat, 2)).Append("% (calc.)");
                                else
                                    str.Append(batteryTimeCache);
                                
                                str.AppendLine();
                                
                                
                                
                                
                                
                                
                                
                                
                                
                                str.Append(LCD_PAD).Append("Oxygen: ");
                                
                                float o2 = MyHud.CharacterInfo.OxygenLevel * 100;
                                
                                if(o2 != prevBattery)
                                {
                                    long now = DateTime.UtcNow.Ticks;
                                    float elapsed = (float)TimeSpan.FromTicks(now - prevBatteryTime).TotalSeconds;
                                    float diff = o2 - prevBattery;
                                    float eta;
                                    tmp.Clear();
                                    
                                    if(prevBattery > o2)
                                    {
                                        eta = (0 - o2) / (diff / elapsed);
                                    }
                                    else
                                    {
                                        tmp.Append("+");
                                        eta = (100 - o2) / (diff / elapsed);
                                    }
                                    
                                    tmp.Append(Math.Round(o2, 2)).Append("% (");
                                    MyValueFormatter.AppendTimeInBestUnit(eta, tmp);
                                    tmp.Append(")");
                                    batteryTimeCache = tmp.ToString();
                                    tmp.Clear();
                                    
                                    prevBatteryTime = now;
                                    prevBattery = o2;
                                }
                                
                                if(batteryTimeCache == null)
                                    str.Append(Math.Round(o2, 2)).Append("% (calc.)");
                                else
                                    str.Append(batteryTimeCache);
                                
                                str.AppendLine();
                                
                                
                                
                                
                                
                                
                                
                                
                                float h = MyHud.CharacterInfo.HydrogenRatio * 100;
                                str.Append(LCD_PAD).Append("Hydrogen: ").Append((int)h).Append("% (?s)").AppendLine();
                                 */
                                
                                str.AppendLine();
                                
                                //str.Append(LCD_PAD).Append("FPS: "+MyHud.Netgraph.FramesPerSecond+"; UPS: "+MyHud.Netgraph.UpdatesPerSecond+"; Ping:"+MyHud.Netgraph.Ping).AppendLine();
                            }
                            
                            str.AppendLine();
                            
                            bool cubeBuilder = MyAPIGateway.CubeBuilder.IsActivated;
                            bool buildTool = false;
                            bool drill = false;
                            bool weapon = false;
                            
                            if(!cubeBuilder && !inShip && holdingTool != null)
                            {
                                if(holdingTool.Closed || holdingTool.MarkedForClose)
                                {
                                    holdingTool = null;
                                    holdingToolTypeId = null;
                                    holdingToolSubtypeId = null;
                                }
                                else
                                {
                                    switch(holdingToolTypeId)
                                    {
                                        case "MyObjectBuilder_Welder":
                                        case "MyObjectBuilder_AngleGrinder":
                                            buildTool = true;
                                            break;
                                        case "MyObjectBuilder_HandDrill":
                                            drill = true;
                                            break;
                                        case "MyObjectBuilder_AutomaticRifle":
                                            weapon = true;
                                            break;
                                    }
                                }
                            }
                            
                            if(cubeBuilder || buildTool)
                            {
                                var b = MyHud.BlockInfo;
                                
                                if(b.BlockName != null && b.BlockName.Length > 0)
                                {
                                    int hp = (int)Math.Floor(b.BlockIntegrity * 100);
                                    int crit = (int)Math.Floor(b.CriticalIntegrity * 100);
                                    int own = (int)Math.Floor(b.OwnershipIntegrity * 100);
                                    
                                    if(!cubeBuilder && hp == 0 && crit == 0 && own == 0)
                                    {
                                        str.Append("Waiting for selection...").AppendLine();
                                    }
                                    else
                                    {
                                        str.Append(MyHud.BlockInfo.BlockName).AppendLine();
                                        
                                        if(!cubeBuilder)
                                            str.Append("Integrity: ").Append(hp).Append("% (").Append(crit).Append("% / ").Append(own).Append("%)").AppendLine();
                                        
                                        components.Clear();
                                        
                                        foreach(var comp in b.Components)
                                        {
                                            if(comp.TotalCount > comp.InstalledCount)
                                            {
                                                if(components.ContainsKey(comp.DefinitionId.SubtypeName))
                                                    components[comp.DefinitionId.SubtypeName] += (comp.TotalCount - comp.InstalledCount);
                                                else
                                                    components.Add(comp.DefinitionId.SubtypeName, (comp.TotalCount - comp.InstalledCount));
                                            }
                                        }
                                        
                                        int componentsCount = components.Count;
                                        
                                        if(componentsCount > 0)
                                        {
                                            int max = (cubeBuilder ? 4 : 3);
                                            int line = 0;
                                            
                                            foreach(var comp in components)
                                            {
                                                str.Append("  ").Append(comp.Value).Append("x ").Append(comp.Key).AppendLine();
                                                
                                                if(++line >= max)
                                                    break;
                                            }
                                            
                                            if(componentsCount > max)
                                                str.Append(" +").Append(components.Count - 3).Append(" other...").AppendLine();
                                            
                                            components.Clear();
                                        }
                                    }
                                }
                                else
                                {
                                    str.Append("Waiting for selection...").AppendLine();
                                }
                            }
                            else if(drill)
                            {
                                str.Append("Ore in range: ").Append(MyHud.OreMarkers.Count()).AppendLine();
                            }
                            else if(weapon)
                            {
                                var obj = holdingTool.GetObjectBuilder(false) as MyObjectBuilder_AutomaticRifle;
                                var physDef = MyDefinitionManager.Static.GetPhysicalItemForHandItem(obj.GetId());
                                var physWepDef = physDef as MyWeaponItemDefinition;
                                MyWeaponDefinition wepDef;
                                
                                if(physWepDef != null && MyDefinitionManager.Static.TryGetWeaponDefinition(physWepDef.WeaponDefinitionId, out wepDef))
                                {
                                    str.Append(physDef.DisplayNameText).AppendLine();
                                    
                                    MyInventory inv;
                                    
                                    if((MyAPIGateway.Session.ControlledObject.Entity as MyEntity).TryGetInventory(out inv))
                                    {
                                        var currentMag = obj.GunBase.CurrentAmmoMagazineName;
                                        var types = wepDef.AmmoMagazinesId.Length;
                                        
                                        float rounds = 0;
                                        tmp.Clear();
                                        
                                        for(int i = 0; i < types; i++)
                                        {
                                            var magId = wepDef.AmmoMagazinesId[i];
                                            var magDef = MyDefinitionManager.Static.GetAmmoMagazineDefinition(magId);
                                            
                                            if(types > 1)
                                            {
                                                if(magId.SubtypeName == currentMag)
                                                    str.Append("[x] ");
                                                else
                                                    str.Append("[ ] ");
                                            }
                                            
                                            float mags = (float)inv.GetItemAmount(wepDef.AmmoMagazinesId[i], MyItemFlags.None);
                                            string magName = magDef.DisplayNameText;
                                            
                                            if(magName.Length > 16)
                                                magName = magName.Substring(0, 20)+"..";
                                            
                                            if(magId.SubtypeName == currentMag)
                                                rounds = obj.GunBase.RemainingAmmo + mags * magDef.Capacity;
                                            
                                            tmp.Append(mags).Append("x ").Append(magName).AppendLine();
                                        }
                                        
                                        str.Append("Rounds: ").Append(rounds).AppendLine();
                                        str.Append(tmp);
                                        tmp.Clear();
                                    }
                                    else
                                    {
                                        str.AppendLine("Error getting character inventory.");
                                    }
                                }
                            }
                            else if(inShip)
                            {
                                var controlled = MyAPIGateway.Session.ControlledObject as Ingame.IMyShipController;
                                
                                if(controlled != null)
                                {
                                    var blockName = controlled.CustomName.ToLower();
                                    
                                    if(blockName.Contains("ores"))
                                    {
                                        str.Append("Ore in range: ").Append(MyHud.OreMarkers.Count()).AppendLine();
                                    }
                                    
                                    bool showGatling = blockName.Contains("gatling");
                                    bool showLaunchers = blockName.Contains("launchers");
                                    bool showAmmo = blockName.Contains("ammo");
                                    bool showCargo = blockName.Contains("cargo");
                                    bool showOxygen = blockName.Contains("oxygen");
                                    bool showHydrogen = blockName.Contains("hydrogen");
                                    
                                    if(showGatling || showLaunchers || showAmmo || showCargo || showOxygen || showHydrogen)
                                    {
                                        var grid = controlled.CubeGrid as IMyCubeGrid;
                                        
                                        blocks.Clear();
                                        grid.GetBlocks(blocks, b => b.FatBlock != null);
                                        
                                        float cargo = 0;
                                        float totalCargo = 0;
                                        float cargoMass = 0;
                                        int containers = 0;
                                        float mags = 0;
                                        int bullets = 0;
                                        float missiles = 0;
                                        
                                        float oxygen = 0;
                                        int oxygenTanks = 0;
                                        
                                        float hydrogen = 0;
                                        int hydrogenTanks = 0;
                                        
                                        int gatlingGuns = 0;
                                        float gatlingAmmo = 0;
                                        
                                        int launchers = 0;
                                        float launchersAmmo = 0;
                                        
                                        //Dictionary<MyDefinitionId, int> searchAmmo = new Dictionary<MyDefinitionId, int>();
                                        MyInventory inv;
                                        
                                        foreach(var slim in blocks)
                                        {
                                            if(showGatling && slim.FatBlock is Ingame.IMySmallGatlingGun)
                                            {
                                                var obj = slim.FatBlock.GetObjectBuilderCubeBlock(false) as MyObjectBuilder_SmallGatlingGun;
                                                var def = MyDefinitionManager.Static.GetCubeBlockDefinition(slim.FatBlock.BlockDefinition) as MyWeaponBlockDefinition;
                                                var wepDef = MyDefinitionManager.Static.GetWeaponDefinition(def.WeaponDefinitionId);
                                                var currentMag = obj.GunBase.CurrentAmmoMagazineName;
                                                var types = wepDef.AmmoMagazinesId.Length;
                                                
                                                gatlingGuns++;
                                                gatlingAmmo += obj.GunBase.RemainingAmmo;
                                                
                                                for(int i = 0; i < types; i++)
                                                {
                                                    var magId = wepDef.AmmoMagazinesId[i];
                                                    
                                                    if(magId.SubtypeName == currentMag)
                                                    {
                                                        var magDef = MyDefinitionManager.Static.GetAmmoMagazineDefinition(magId);
                                                        
                                                        if((slim.FatBlock as MyEntity).TryGetInventory(out inv))
                                                            gatlingAmmo += (float)inv.GetItemAmount(magDef.Id, MyItemFlags.None) * magDef.Capacity;
                                                        
                                                        //if(!searchAmmo.ContainsKey(magId))
                                                        //    searchAmmo.Add(magId, magDef.Capacity);
                                                        
                                                        break;
                                                    }
                                                }
                                            }
                                            else if(showLaunchers && slim.FatBlock is Ingame.IMySmallMissileLauncher)
                                            {
                                                var obj = slim.FatBlock.GetObjectBuilderCubeBlock(false) as MyObjectBuilder_SmallMissileLauncher;
                                                var def = MyDefinitionManager.Static.GetCubeBlockDefinition(slim.FatBlock.BlockDefinition) as MyWeaponBlockDefinition;
                                                var wepDef = MyDefinitionManager.Static.GetWeaponDefinition(def.WeaponDefinitionId);
                                                var currentMag = obj.GunBase.CurrentAmmoMagazineName;
                                                var types = wepDef.AmmoMagazinesId.Length;
                                                
                                                launchers++;
                                                launchersAmmo += obj.GunBase.RemainingAmmo;
                                                
                                                for(int i = 0; i < types; i++)
                                                {
                                                    var magId = wepDef.AmmoMagazinesId[i];
                                                    
                                                    if(magId.SubtypeName == currentMag)
                                                    {
                                                        var magDef = MyDefinitionManager.Static.GetAmmoMagazineDefinition(magId);
                                                        
                                                        if((slim.FatBlock as MyEntity).TryGetInventory(out inv))
                                                            launchersAmmo += (float)inv.GetItemAmount(magDef.Id, MyItemFlags.None) * magDef.Capacity;
                                                        
                                                        break;
                                                    }
                                                }
                                            }
                                            else if(showCargo && slim.FatBlock is Ingame.IMyCargoContainer)
                                            {
                                                if((slim.FatBlock as MyEntity).TryGetInventory(out inv))
                                                {
                                                    cargo += (float)inv.CurrentVolume;
                                                    totalCargo += (float)inv.MaxVolume;
                                                    cargoMass += (float)inv.CurrentMass;
                                                    containers++;
                                                }
                                            }
                                            else if(showAmmo && (slim.FatBlock is Ingame.IMyLargeTurretBase || slim.FatBlock is Ingame.IMySmallGatlingGun || slim.FatBlock is Ingame.IMyCargoContainer))
                                            {
                                                if((slim.FatBlock as MyEntity).TryGetInventory(out inv))
                                                {
                                                    mags += (float)inv.GetItemAmount(AMMO_BULLETS.GetId(), MyItemFlags.None);
                                                    missiles += (float)inv.GetItemAmount(AMMO_MISSILES.GetId(), MyItemFlags.None);
                                                }
                                            }
                                            else if((showOxygen || showHydrogen) && slim.FatBlock is Ingame.IMyOxygenTank)
                                            {
                                                var tank = slim.FatBlock as Ingame.IMyOxygenTank;
                                                var def = MyDefinitionManager.Static.GetCubeBlockDefinition(tank.BlockDefinition) as MyGasTankDefinition;
                                                
                                                if(showHydrogen && def.StoredGasId.SubtypeName == "Hydrogen")
                                                {
                                                    hydrogen += tank.GetOxygenLevel();
                                                    hydrogenTanks += 1;
                                                }
                                                else if(showOxygen && def.StoredGasId.SubtypeName == "Oxygen")
                                                {
                                                    oxygen += tank.GetOxygenLevel();
                                                    oxygenTanks += 1;
                                                }
                                            }
                                        }
                                        
                                        if(showGatling)
                                        {
                                            //foreach(var slim in blocks)
                                            //{
                                            //    if(slim.FatBlock is Ingame.IMyCargoContainer)
                                            //    {
                                            //        if((slim.FatBlock as MyEntity).TryGetInventory(out inv))
                                            //        {
                                            //            foreach(var kv in searchAmmo)
                                            //            {
                                            //                gatlingAmmo += (float)inv.GetItemAmount(kv.Key, MyItemFlags.None) * kv.Value;
                                            //            }
                                            //        }
                                            //    }
                                            //}
                                            
                                            str.Append(gatlingGuns).Append("x Gatling Gun: ").Append(Math.Floor(gatlingAmmo)).AppendLine();
                                        }
                                        
                                        if(showLaunchers)
                                        {
                                            str.Append(launchers).Append("x Missile Launcher: ").Append(Math.Floor(launchersAmmo)).AppendLine();
                                        }
                                        
                                        if(showAmmo)
                                        {
                                            str.Append(mags).Append("x 25x184 mags (").Append(bullets).Append(")").AppendLine();
                                            str.Append(missiles).Append("x 200mm missiles").AppendLine();
                                        }
                                        
                                        if(showCargo)
                                        {
                                            if(containers == 0)
                                                str.Append("No cargo containers");
                                            else
                                                str.Append(containers).Append("x Container: ").Append(Math.Round((cargo / totalCargo) * 100f, 2)).Append("% (").Append(cargoMass).Append("kg)");
                                            str.AppendLine();
                                        }
                                        
                                        if(showOxygen)
                                        {
                                            if(oxygenTanks == 0)
                                                str.Append("No oxygen tanks.");
                                            else
                                                str.Append(oxygenTanks).Append("x Oxygen tank: ").Append(Math.Round((oxygen / oxygenTanks) * 100f, 2)).Append("%");
                                            str.AppendLine();
                                        }
                                        
                                        if(showHydrogen)
                                        {
                                            if(hydrogenTanks == 0)
                                                str.Append("No hydrogen tanks");
                                            else
                                                str.Append(hydrogenTanks).Append("x Hydrogen tank: ").Append(Math.Round((hydrogen / hydrogenTanks) * 100f, 2)).Append("%");
                                            str.AppendLine();
                                        }
                                    }
                                }
                            }
                        }
                        catch(Exception e)
                        {
                            str.Append("ERROR, SEND LOG TO AUTHOR.").AppendLine();
                            Log.Error(e);
                        }
                        
                        string text = str.ToString();
                        str.Clear();
                        
                        if(lastDisplayText == null || !text.Equals(lastDisplayText))
                        {
                            lastDisplayText = text;
                            lcd.ShowTextureOnScreen();
                            lcd.WritePublicText(text, false);
                            lcd.ShowPublicTextOnScreen();
                        }
                    }
                }
                
                if(!element.hasBar)
                    return; // if the HUD element has no bar, stop here.
                
                if(iconBarEntities[id] == null)
                {
                    iconBarEntities[id] = SpawnPrefab(settings.GetRenderPrefix() + CUBE_HUD_PREFIX + name + "Bar");
                    
                    if(iconBarEntities[id] == null)
                        return;
                }
                
                // blink the bar along with the warning icon
                if(!warningBlinkOn && percent <= element.warnPercent)
                {
                    matrix.Translation += (matrix.Backward * 10);
                    iconBarEntities[id].SetWorldMatrix(matrix);
                    return;
                }
                
                // calculate the bar size
                double scale = Math.Min(Math.Max((percent * HUD_BAR_MAX_SCALE) / 100, 0), HUD_BAR_MAX_SCALE);
                var align = (element.flipHorizontal ? matrix.Left : matrix.Right);
                matrix.Translation += (align * (scale / 100.0)) - (align * (0.00955 + (0.0008 * (0.5 - (percent / 100)))));
                matrix.M11 *= scale;
                matrix.M12 *= scale;
                matrix.M13 *= scale;
                
                iconBarEntities[id].SetWorldMatrix(matrix);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        private void AlignToVector(ref MatrixD matrix, Vector3D direction)
        {
            Vector3D vector3D = new Vector3D(0.0, 0.0, 1.0);
            Vector3D up;
            double z = direction.Z;
            
            if (z > -0.99999 && z < 0.99999)
            {
                vector3D -= direction * z;
                vector3D = Vector3D.Normalize(vector3D);
                up = Vector3D.Cross(direction, vector3D);
            }
            else
            {
                vector3D = new Vector3D(direction.Z, 0.0, -direction.X);
                up = new Vector3D(0.0, 1.0, 0.0);
            }
            
            matrix.Right = vector3D;
            matrix.Up = up;
            matrix.Forward = direction;
            MatrixD.Rescale(ref matrix, -1); // CreateFromDir() makes the mesh's faces inverted, this fixes that
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
        
        private IMyEntity SpawnPrefab(string name, bool isDisplay = false)
        {
            try
            {
                if(isDisplay)
                {
                    PrefabBuilder.CubeBlocks[0] = new MyObjectBuilder_TextPanel()
                    {
                        EntityId = 1,
                        SubtypeName = name,
                        Min = PrefabVectorI0,
                        BlockOrientation = PrefabOrientation,
                        ShareMode = MyOwnershipShareModeEnum.None,
                        DeformationRatio = 0,
                        ShowOnHUD = false,
                        ShowText = ShowTextOnScreenFlag.PUBLIC,
                        FontSize = DISPLAY_FONT_SIZE,
                        FontColor = settings.displayFontColor,
                        BackgroundColor = settings.displayBgColor,
                    };
                    
                    if(characterEntity != null)
                    {
                        var charObj = characterEntity.GetObjectBuilder(false) as MyObjectBuilder_Character;
                        PrefabBuilder.CubeBlocks[0].ColorMaskHSV = charObj.ColorMaskHSV;
                    }
                    
                    PrefabBuilder.CubeBlocks.Add(PrefabBattery);
                }
                else
                    PrefabBuilder.CubeBlocks[0].SubtypeName = name;
                
                MyAPIGateway.Entities.RemapObjectBuilder(PrefabBuilder);
                var ent = MyAPIGateway.Entities.CreateFromObjectBuilder(PrefabBuilder);
                ent.Flags &= ~EntityFlags.Sync; // don't sync on MP
                ent.Flags &= ~EntityFlags.Save; // don't save this entity
                ent.PersistentFlags &= ~MyPersistentEntityFlags2.CastShadows;
                ent.CastShadows = false;
                
                MyAPIGateway.Entities.AddEntity(ent, true);
                return ent;
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
            
            return null;
        }
        
        private static SerializableVector3 PrefabVector0 = new SerializableVector3(0,0,0);
        private static SerializableVector3I PrefabVectorI0 = new SerializableVector3I(0,0,0);
        private static SerializableBlockOrientation PrefabOrientation = new SerializableBlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up);
        private static MyObjectBuilder_CubeGrid PrefabBuilder = new MyObjectBuilder_CubeGrid()
        {
            EntityId = 0,
            GridSizeEnum = MyCubeSize.Small,
            IsStatic = true,
            Skeleton = new List<BoneInfo>(),
            LinearVelocity = PrefabVector0,
            AngularVelocity = PrefabVector0,
            ConveyorLines = new List<MyObjectBuilder_ConveyorLine>(),
            BlockGroups = new List<MyObjectBuilder_BlockGroup>(),
            Handbrake = false,
            XMirroxPlane = null,
            YMirroxPlane = null,
            ZMirroxPlane = null,
            PersistentFlags = MyPersistentEntityFlags2.InScene,
            Name = "",
            DisplayName = "",
            CreatePhysics = false,
            PositionAndOrientation = new MyPositionAndOrientation(Vector3D.Zero, Vector3D.Forward, Vector3D.Up),
            CubeBlocks = new List<MyObjectBuilder_CubeBlock>()
            {
                new MyObjectBuilder_TerminalBlock()
                {
                    EntityId = 1,
                    SubtypeName = "",
                    Min = PrefabVectorI0,
                    BlockOrientation = PrefabOrientation,
                    ShareMode = MyOwnershipShareModeEnum.None,
                    DeformationRatio = 0,
                    ShowOnHUD = false,
                }
            }
        };
        private static MyObjectBuilder_BatteryBlock PrefabBattery = new MyObjectBuilder_BatteryBlock()
        {
            SubtypeName = CUBE_HUD_PREFIX + "battery",
            Min = new SerializableVector3I(0,0,-10),
            BlockOrientation = PrefabOrientation,
            ShareMode = MyOwnershipShareModeEnum.None,
            DeformationRatio = 0,
            ShowOnHUD = false,
            CurrentStoredPower = float.MaxValue,
            MaxStoredPower = float.MaxValue,
        };
        
        public static void GetGravityAtPoint(Vector3D point, ref Vector3 gravity, ref float g, ref int sources)
        {
            gravity = Vector3.Zero;
            g = 0;
            sources = 0;
            
            try
            {
                foreach(var generator in gravityGenerators)
                {
                    if(generator.IsWorking)
                    {
                        if(generator is IMyGravityGeneratorSphere)
                        {
                            var gen = (generator as IMyGravityGeneratorSphere);
                            
                            if(Vector3D.DistanceSquared(generator.WorldMatrix.Translation, point) <= (gen.Radius * gen.Radius))
                            {
                                var dir = generator.WorldMatrix.Translation - point;
                                dir.Normalize();
                                gravity += (Vector3)dir * (gen.Gravity / 9.81f); // TODO: remove division once gravity value is fixed
                                sources++;
                            }
                        }
                        else if(generator is IMyGravityGenerator)
                        {
                            var gen = (generator as IMyGravityGenerator);
                            
                            var halfExtents = new Vector3(gen.FieldWidth / 2, gen.FieldHeight / 2, gen.FieldDepth / 2);
                            var box = new MyOrientedBoundingBoxD(gen.WorldMatrix.Translation, halfExtents, Quaternion.CreateFromRotationMatrix(gen.WorldMatrix));
                            
                            if(box.Contains(ref point))
                            {
                                gravity += gen.WorldMatrix.Down * gen.Gravity;
                                sources++;
                            }
                        }
                    }
                }
                
                g = (float)gravity.Length();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
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
                        MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Invalid float number: " + msg);
                    
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
                    if(settings.Load())
                        MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Reloaded and re-saved config.");
                    else
                        MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Config created with the current settings.");
                    
                    settings.Save();
                    RemoveHelmet();
                    return;
                }
                else if(msg.StartsWith("dx9"))
                {
                    MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Configured for DX9; saved to config.");
                    settings.renderer = Renderer.DX9;
                    settings.Save();
                    RemoveHelmet();
                    return;
                }
                else if(msg.StartsWith("dx11"))
                {
                    MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Configured for DX11; saved to config.");
                    settings.renderer = Renderer.DX11;
                    settings.Save();
                    RemoveHelmet();
                    return;
                }
                else if(msg.StartsWith("glass off"))
                {
                    MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Glass reflections turned OFF; saved to config.");
                    settings.glass = false;
                    settings.Save();
                    RemoveHelmet();
                    return;
                }
                else if(msg.StartsWith("glass on"))
                {
                    MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Glass reflections turned ON; saved to config.");
                    settings.glass = true;
                    settings.Save();
                    RemoveHelmet();
                    return;
                }
            }
            
            MyAPIGateway.Utilities.ShowMissionScreen("Helmet Mod Commands", "", "You can type these commands in the chat.", HELP_COMMANDS, null, "Close");
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid))]
    public class Grid : MyGameLogicComponent
    {
        private MyObjectBuilder_EntityBase objectBuilder;
        
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            this.objectBuilder = objectBuilder;
            
            Entity.NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }
        
        public override void UpdateOnceBeforeFrame()
        {
            var grid = Entity as IMyCubeGrid;
            
            // find existing gravity generators
            List<IMySlimBlock> blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks, b => b.FatBlock is IMyGravityGeneratorBase);
            
            foreach(var slimBlock in blocks)
            {
                Helmet.gravityGenerators.Add(slimBlock.FatBlock as IMyGravityGeneratorBase);
            }
            
            // monitor generator adding/removing
            grid.OnBlockAdded += BlockAdded;
            grid.OnBlockRemoved += BlockRemoved;
        }
        
        public void BlockAdded(IMySlimBlock slimBlock)
        {
            if(slimBlock.FatBlock is IMyGravityGeneratorBase)
            {
                Helmet.gravityGenerators.Add(slimBlock.FatBlock as IMyGravityGeneratorBase);
            }
        }
        
        public void BlockRemoved(IMySlimBlock slimBlock)
        {
            if(slimBlock.FatBlock is IMyGravityGeneratorBase)
            {
                Helmet.gravityGenerators.Remove(slimBlock.FatBlock as IMyGravityGeneratorBase);
            }
        }
        
        public override void Close()
        {
            // grid removed, clean stuff
            Helmet.gravityGenerators.RemoveAll(g => g.CubeGrid.EntityId == Entity.EntityId);
            objectBuilder = null;
        }
        
        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return copy ? (MyObjectBuilder_EntityBase)objectBuilder.Clone() : objectBuilder;
        }
    }

    public static class Extensions
    {
        public static string ToUpperFirst(this string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            
            char[] a = s.ToCharArray();
            a[0] = char.ToUpper(a[0]);
            return new string(a);
        }
    }
}