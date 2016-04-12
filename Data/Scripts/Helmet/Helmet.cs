using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.Engine;
using Sandbox.Engine.Physics;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.Input;
using VRageMath;
using VRage;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using Digi.Utils;
using Digi.Helmet;
using VRageRender;
using Ingame = Sandbox.ModAPI.Ingame;
using IMyControllableEntity = VRage.Game.ModAPI.Interfaces.IMyControllableEntity;

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
        private bool warningBlinkOn = true;
        private double lastWarningBlink = 0;
        private long helmetBroken = 0;
        private bool prevHelmetOn = false;
        private long animationStart = 0;
        
        private float oldFov = 60;
        
        public static Dictionary<long, IMyGravityGeneratorBase> gravityGenerators = new Dictionary<long, IMyGravityGeneratorBase>();
        private Vector3 artificialDir = Vector3.Zero;
        private Vector3 naturalDir = Vector3.Zero;
        private Vector3 gravityDir = Vector3.Zero;
        private float artificialForce = 0;
        private float naturalForce = 0;
        private float gravityForce = 0;
        private float altitude = 0;
        private float inventoryMass = 0;
        
        private IMyEntity vectorGravity = null;
        private IMyEntity vectorVelocity = null;
        
        private short tick = 0;
        private const int SKIP_TICKS_FOV = 60;
        private const int SKIP_TICKS_HUD = 5;
        private const int SKIP_TICKS_GRAVITY = 2;
        private const int SKIP_TICKS_PLANETS = 60*3;
        
        private MatrixD helmetMatrix;
        private float[] values = new float[Settings.TOTAL_ELEMENTS];
        private int[] show = new int[Settings.TOTAL_ELEMENTS];
        private MatrixD lastMatrix;
        private int lastOxygenEnv = 0;
        private bool fatalError = false;
        
        public static IMyEntity holdingTool = null;
        public static MyObjectBuilderType holdingToolTypeId;
        
        public IMyEntity[] iconEntities = new IMyEntity[Settings.TOTAL_ELEMENTS];
        public IMyEntity[] iconBarEntities = new IMyEntity[Settings.TOTAL_ELEMENTS];
        
        double lastLinearSpeed;
        Vector3D lastDirSpeed;
        
        private int skipDisplay = 0;
        private float prevSpeed = 0;
        
        private float prevBattery = -1;
        private long prevBatteryTime = 0;
        private float prevO2 = -1;
        private long prevO2Time = 0;
        private float prevH = -1;
        private long prevHTime = 0;
        private float etaPower = 0;
        private float etaO2 = 0;
        private float etaH = 0;
        
        private StringBuilder str = new StringBuilder();
        private const string DISPLAY_PAD = " ";
        private const float DISPLAY_FONT_SIZE = 1.3f;
        private const string NUMBER_FORMAT = "###,###,##0";
        private const string FLOAT_FORMAT = "###,###,##0.##";
        
        public Dictionary<long, MyPlanet> planets = new Dictionary<long, MyPlanet>();
        private static HashSet<IMyEntity> ents = new HashSet<IMyEntity>(); // this is always empty
        
        private Random rand = new Random();
        private Dictionary<string, int> components = new Dictionary<string, int>();
        private List<IMySlimBlock> blocks = new List<IMySlimBlock>();
        private StringBuilder tmp = new StringBuilder();
        
        public static bool displayUpdate = true;
        public static string displayText = "";
        private string lastDisplayText = null;
        
        private HashSet<MyStringHash> glassBreakCauses = new HashSet<MyStringHash>()
        {
            MyDamageType.Environment,
            MyDamageType.Explosion,
            MyDamageType.Bullet,
            MyDamageType.Drill,
            MyDamageType.Fall,
            MyDamageType.Rocket,
            MyDamageType.Weapon,
            MyDamageType.Mine,
            MyDamageType.Bolt,
            MyDamageType.Squeez
        };
        
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
            "/helmet lcd <on/off>   turns the LCD on or off\n" +
            "\n" +
            "For advanced editing go to:\n" +
            "%appdata%\\SpaceEngineers\\Storage\\428842256_Helmet\\helmet.cfg";
        
        //private const string FIRSTRUN_TITLE = "Helmet mod";
        //private const string FIRSTRUN_SUB = "TL;DR: Running DX11 ? Type in chat: /helmet dx11";
        //private const string FIRSTRUN_TEXT = "\nThe mod has individual tweaks for DX9 and DX11, because it can't detect what you're running it's set for DX9 by default.\n\nIf you're running DX11 type /helmet dx11 in chat!";
        
        public void Init()
        {
            init = true;
            isServer = MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE || MyAPIGateway.Multiplayer.IsServer;
            isDedicated = (MyAPIGateway.Utilities.IsDedicated && isServer);
            
            Log.Init();
            Log.Info("Initialized");
            
            if(!isDedicated)
            {
                settings = new Settings();
                
                MyAPIGateway.Entities.OnEntityAdd += EntityAdded;
                MyAPIGateway.Utilities.MessageEntered += MessageEntered;
                MyAPIGateway.Session.DamageSystem.RegisterDestroyHandler(999, EntityKilled);
            }
        }
        
        protected override void UnloadData()
        {
            try
            {
                if(init)
                {
                    init = false;
                    ents.Clear();
                    planets.Clear();
                    gravityGenerators.Clear();
                    holdingTool = null;
                    
                    if(!isDedicated)
                    {
                        MyAPIGateway.Entities.OnEntityAdd -= EntityAdded;
                        MyAPIGateway.Utilities.MessageEntered -= MessageEntered;
                        
                        if(settings != null)
                        {
                            settings.Close();
                            settings = null;
                        }
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
            
            Log.Info("Mod unloaded.");
            Log.Close();
        }
        
        public void EntityAdded(IMyEntity ent) // executed only on player hosted server's clients
        {
            if(ent is IMyCubeGrid)
            {
                var grid = ent as IMyCubeGrid;
                
                if(grid.IsStatic && grid.GridSizeEnum == MyCubeSize.Small && grid.Name.StartsWith("helmetmod_helmet_server_"))
                {
                    ent.Close(); // remove server's helmet from clients...
                }
            }
        }
        
        public void EntityKilled(object obj, MyDamageInformation info)
        {
            if(characterEntity != null && obj is IMyCharacter)
            {
                var ent = obj as IMyEntity;
                
                if(ent.EntityId == characterEntity.EntityId && glassBreakCauses.Contains(info.Type))
                {
                    helmetBroken = ent.EntityId;
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
            
            tick++; // global update tick
            
            if(characterEntity != null)
            {
                if(characterEntity.MarkedForClose || characterEntity.Closed)
                {
                    characterEntity = null;
                }
                else if(settings.autoFovScale)
                {
                    if(tick % SKIP_TICKS_FOV == 0)
                    {
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
            
            if(camera != null && MyAPIGateway.Session.ControlledObject != null && MyAPIGateway.Session.ControlledObject.Entity != null)
            {
                var contrEnt = MyAPIGateway.Session.ControlledObject.Entity;
                bool attach = false;
                
                if(camera is IMyCharacter)
                {
                    characterEntity = camera as IMyEntity;
                    attach = true;
                }
                else if(contrEnt is Ingame.IMyShipController && camera is Ingame.IMyShipController)
                {
                    attach = true;
                    
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
                    
                    if(settings.toggleHelmetInCockpit && characterEntity != null && contrEnt is Ingame.IMyCockpit)
                    {
                        if(MyGuiScreenGamePlay.ActiveGameplayScreen == null && MyGuiScreenTerminal.GetCurrentScreen() == MyTerminalPageEnum.None)
                        {
                            if(MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.HELMET))
                            {
                                (characterEntity as Sandbox.Game.Entities.IMyControllableEntity).SwitchHelmet();
                            }
                        }
                    }
                }
                
                if(characterEntity != null)
                {
                    bool helmetOn = (characterEntity as IMyControllableEntity).EnabledHelmet;
                    
                    if(helmetOn != prevHelmetOn)
                    {
                        if(settings.animateSpeed > 0)
                            animationStart = DateTime.UtcNow.Ticks; // set start of animation here to avoid animating after going in first person
                        else if(!helmetOn)
                            RemoveHelmet(false);
                        
                        prevHelmetOn = helmetOn;
                    }
                }
                
                if(attach && camera.IsInFirstPersonView && AttachHelmet())
                {
                    return;
                }
            }
            
            RemoveHelmet();
        }
        
        private bool AttachHelmet()
        {
            var controllable = characterEntity as IMyControllableEntity;
            
            if(controllable != null)
            {
                // ignoring head Y axis on foot because of an issue with ALT and 3rd person camera bumping into the ground
                bool inCockpit = !(MyAPIGateway.Session.CameraController is IMyCharacter); // (MyAPIGateway.Session.ControlledObject.Entity is IMyCockpit);
                UpdateHelmetAt(MyAPIGateway.Session.ControlledObject.GetHeadMatrix(inCockpit, true));
                return true;
            }
            
            return false;
        }
        
        private void RemoveHelmet(bool removeHud = true)
        {
            if(!removedHelmet)
            {
                removedHelmet = true;
                
                if(helmet != null)
                {
                    if(characterEntity != null)
                        helmet.SetPosition(characterEntity.WorldMatrix.Translation + characterEntity.WorldMatrix.Backward * 1000);
                    else
                        helmet.SetPosition(Vector3D.Zero);
                    
                    helmet.Close();
                    helmet = null;
                    animationStart = 0;
                }
            }
            
            if(removeHud)
                RemoveHud();
        }
        
        private void RemoveHud()
        {
            if(removedHUD)
                return;
            
            removedHUD = true;
            Vector3D pos = Vector3D.Zero;
            
            if(characterEntity != null)
                pos = characterEntity.WorldMatrix.Translation + characterEntity.WorldMatrix.Backward * 1000;
            
            for(int id = 0; id < Settings.TOTAL_ELEMENTS; id++)
            {
                if(iconEntities[id] != null)
                {
                    iconEntities[id].SetPosition(pos);
                    iconEntities[id].Close();
                    iconEntities[id] = null;
                }
                
                if(id == Icons.VECTOR)
                {
                    if(vectorGravity != null)
                    {
                        vectorGravity.SetPosition(pos);
                        vectorGravity.Close();
                        vectorGravity = null;
                    }
                    
                    if(vectorVelocity != null)
                    {
                        vectorVelocity.SetPosition(pos);
                        vectorVelocity.Close();
                        vectorVelocity = null;
                    }
                }
                
                if(iconBarEntities[id] != null)
                {
                    iconBarEntities[id].SetPosition(pos);
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
                
                bool helmetOn = (characterEntity as IMyControllableEntity).EnabledHelmet;
                bool brokenHelmet = (helmetBroken > 0 && helmetBroken == characterEntity.EntityId);
                
                if(helmetOn && brokenHelmet)
                {
                    RemoveHelmet();
                    helmetBroken = 0;
                }
                
                if(helmetOn)
                {
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
                }
                
                if(!(MyAPIGateway.Session.CameraController is Ingame.IMyCockpit)) // No smoothing when camera is in cockpit/passenger seat because it already has smoothing
                {
                    if(settings.delayedRotation > 0)
                    {
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
                if(helmet != null)
                {
                    helmetMatrix = matrix;
                    helmetMatrix.Translation += matrix.Forward * (SCALE_DIST_ADJUST * (1.0 - settings.scale));
                    
                    if(animationStart > 0)
                    {
                        float tickDiff = (float)MathHelper.Clamp((DateTime.UtcNow.Ticks - animationStart) / (TimeSpan.TicksPerMillisecond * settings.animateSpeed * 1000), 0, 1);
                        
                        if(tickDiff == 1)
                        {
                            animationStart = 0;
                            
                            if(!helmetOn)
                                RemoveHelmet(false);
                            
                            return;
                        }
                        
                        var toMatrix = MatrixD.CreateWorld(helmetMatrix.Translation + helmetMatrix.Up * 0.5, helmetMatrix.Up, helmetMatrix.Backward);
                        
                        if(helmetOn)
                            helmetMatrix = MatrixD.Slerp(toMatrix, helmetMatrix, tickDiff);
                        else
                            helmetMatrix = MatrixD.Slerp(helmetMatrix, toMatrix, tickDiff);
                    }
                    
                    helmet.SetWorldMatrix(helmetMatrix);
                    removedHelmet = false;
                }
                
                // if HUD is disabled or we can't get the info, remove the HUD (if exists) and stop here
                if(!settings.hud) // || (!helmetOn && !settings.hudAlways)) // || characterObject == null)
                {
                    RemoveHud();
                    return;
                }
                
                var c = MyHud.CharacterInfo;
                
                if(tick % SKIP_TICKS_HUD == 0)
                {
                    // show and value cache for the HUD elements
                    for(int id = 0; id < Settings.TOTAL_ELEMENTS; id++)
                    {
                        values[id] = 0;
                        show[id] = settings.elements[id].show;
                    }
                    
                    bool inShip = MyHud.ShipInfo.Visible;
                    var v = MyHud.ShipInfo;
                    
                    if(settings.elements[Icons.BROADCASTING].show > 0)
                        show[Icons.BROADCASTING] = MyHud.CharacterInfo.BroadcastEnabled ? 1 : 0;
                    
                    if(settings.elements[Icons.DAMPENERS].show > 0)
                        show[Icons.DAMPENERS] = (inShip ? v.DampenersEnabled : c.DampenersEnabled) ? 1 : 0;
                    
                    values[Icons.HEALTH] = (characterEntity is IMyDestroyableObject ? (characterEntity as IMyDestroyableObject).Integrity : 100);
                    values[Icons.ENERGY] = c.BatteryEnergy;
                    values[Icons.HYDROGEN] = c.HydrogenRatio * 100;
                    values[Icons.OXYGEN] = c.OxygenLevel * 100;
                    values[Icons.OXYGEN_ENV] = (characterEntity as IMyCharacter).EnvironmentOxygenLevel * 2;
                    values[Icons.INVENTORY] = 0;
                    inventoryMass = 0;
                    
                    // Get the inventory volume
                    MyInventory inv;
                    
                    if((characterEntity as MyEntity).TryGetInventory(out inv))
                    {
                        values[Icons.INVENTORY] = ((float)inv.CurrentVolume / (float)inv.MaxVolume) * 100;
                        inventoryMass = (float)inv.CurrentMass;
                    }
                }
                
                // Update the warning icon
                show[Icons.WARNING] = 0;
                
                int moveMode = (c.JetpackEnabled ? 2 : 1);
                
                if(settings.elements[Icons.WARNING].show > 0)
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
                                
                                show[Icons.WARNING] = 1;
                                break;
                            }
                        }
                    }
                }
                
                if(show[Icons.VECTOR] > 0 || show[Icons.DISPLAY] > 0 || show[Icons.HORIZON] > 0)
                {
                    if(tick % SKIP_TICKS_PLANETS == 0)
                    {
                        planets.Clear();
                        MyAPIGateway.Entities.GetEntities(ents, delegate(IMyEntity e)
                                                          {
                                                              if(e is MyPlanet)
                                                              {
                                                                  if(!planets.ContainsKey(e.EntityId))
                                                                      planets.Add(e.EntityId, e as MyPlanet);
                                                              }
                                                              
                                                              return false; // no reason to add to the list
                                                          });
                    }
                    
                    if(tick % SKIP_TICKS_GRAVITY == 0)
                    {
                        CalculateGravityAndPlanetsAt(characterEntity.WorldAABB.Center);
                    }
                    
                    var controller = MyAPIGateway.Session.ControlledObject as MyShipController;
                    show[Icons.HORIZON] = ((naturalForce > 0 && controller != null && controller.HorizonIndicatorEnabled) ? 1 : 0);
                }
                
                matrix.Translation += matrix.Forward * (HUDSCALE_DIST_ADJUST * (1.0 - settings.hudScale)); // off-set the HUD according to the HUD scale
                var headPos = matrix.Translation + (matrix.Backward * 0.25); // for glass curve effect
                bool hudVisible = !MyHud.MinimalHud; // if the vanilla HUD is on or off
                removedHUD = false; // mark the HUD as not removed because we're about to spawn it
                
                // spawn and update the HUD elements
                for(int id = 0; id < Settings.TOTAL_ELEMENTS; id++)
                {
                    bool showIcon = false;
                    
                    switch(show[id])
                    {
                        case 1:
                            showIcon = helmetOn || animationStart != 0;
                            break;
                        case 2:
                            showIcon = !helmetOn;
                            break;
                        case 3:
                            showIcon = true;
                            break;
                    }
                    
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
                        iconEntities[id].SetPosition(matrix.Translation + matrix.Backward * 1000);
                        iconEntities[id].Close();
                        iconEntities[id] = null;
                    }
                    
                    if(id == Icons.WARNING)
                        warningBlinkOn = true; // reset the blink status for the warning icon
                    
                    // remove the bar if it has one and it exists
                    if(element.hasBar && iconBarEntities[id] != null)
                    {
                        iconBarEntities[id].SetPosition(matrix.Translation + matrix.Backward * 1000);
                        iconBarEntities[id].Close();
                        iconBarEntities[id] = null;
                    }
                    
                    if(id == Icons.VECTOR)
                    {
                        if(vectorGravity != null)
                        {
                            vectorGravity.SetPosition(matrix.Translation + matrix.Backward * 1000);
                            vectorGravity.Close();
                            vectorGravity = null;
                        }
                        
                        if(vectorVelocity != null)
                        {
                            vectorVelocity.SetPosition(matrix.Translation + matrix.Backward * 1000);
                            vectorVelocity.Close();
                            vectorVelocity = null;
                        }
                    }
                    
                    return; // and STOP!
                }
                
                if(id != Icons.DISPLAY)
                {
                    if(animationStart > 0)
                    {
                        float tickDiff = (float)Math.Min((DateTime.UtcNow.Ticks - animationStart) / (TimeSpan.TicksPerMillisecond * settings.animateSpeed * 1000), 1);
                        var toMatrix = MatrixD.CreateWorld(matrix.Translation + matrix.Up * 0.5, matrix.Up, matrix.Backward);
                        
                        if((characterEntity as IMyControllableEntity).EnabledHelmet)
                            matrix = MatrixD.Slerp(toMatrix, matrix, tickDiff);
                        else
                            matrix = MatrixD.Slerp(matrix, toMatrix, tickDiff);
                    }
                }
                
                // append the oxygen level number to the name and remove the previous entity if changed
                if(id == Icons.OXYGEN_ENV)
                {
                    int oxygenEnv = MathHelper.Clamp((int)percent, 0, 2);
                    
                    if(iconEntities[id] != null && lastOxygenEnv != oxygenEnv)
                    {
                        iconEntities[id].Close();
                        iconEntities[id] = null;
                    }
                    
                    lastOxygenEnv = oxygenEnv;
                }
                
                // spawn the element if it's not already
                if(iconEntities[id] == null)
                {
                    if(id == Icons.DISPLAY)
                    {
                        iconEntities[id] = SpawnPrefab(CUBE_HUD_PREFIX + element.name + (settings.displayQuality == 0 ? "Low" : ""), true);
                        lastDisplayText = null; // force first write
                    }
                    else
                    {
                        string name = element.name;
                        
                        // append the oxygen level number to the name and remove the previous entity if changed
                        if(id == Icons.OXYGEN_ENV)
                        {
                            int oxygenEnv = MathHelper.Clamp((int)percent, 0, 2);
                            name += oxygenEnv.ToString();
                        }
                        
                        iconEntities[id] = SpawnPrefab(settings.GetRenderPrefix() + CUBE_HUD_PREFIX + name);
                    }
                    
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
                
                // update the vector indicator
                if(id == Icons.VECTOR)
                {
                    var vel = characterEntity.Physics.LinearVelocity;
                    
                    if(MyAPIGateway.Session.ControlledObject is Ingame.IMyCockpit)
                    {
                        var grid = (MyAPIGateway.Session.ControlledObject as Ingame.IMyCockpit).CubeGrid;
                        
                        if(grid.Physics != null)
                            vel = grid.Physics.LinearVelocity;
                    }
                    
                    var velL = Math.Round(vel.Length(), 2);
                    
                    bool inGravity = gravityForce > 0;
                    bool isMoving = velL > 0;
                    
                    if(inGravity)
                    {
                        if(vectorGravity == null)
                        {
                            vectorGravity = SpawnPrefab(settings.GetRenderPrefix() + CUBE_HUD_PREFIX + "vectorGravity");
                        }
                    }
                    else
                    {
                        if(vectorGravity != null)
                        {
                            vectorGravity.SetPosition(matrix.Translation + matrix.Backward * 1000);
                            vectorGravity.Close();
                            vectorGravity = null;
                        }
                    }
                    
                    if(isMoving)
                    {
                        if(vectorVelocity == null)
                        {
                            vectorVelocity = SpawnPrefab(settings.GetRenderPrefix() + CUBE_HUD_PREFIX + "vectorVelocity");
                        }
                    }
                    else
                    {
                        if(vectorVelocity != null)
                        {
                            vectorVelocity.SetPosition(matrix.Translation + matrix.Backward * 1000);
                            vectorVelocity.Close();
                            vectorVelocity = null;
                        }
                    }
                    
                    // TODO optimize ?!
                    
                    matrix.Translation += matrix.Forward * 0.05;
                    headPos += matrix.Forward * 0.22;
                    
                    var dirV = Vector3D.Normalize(headPos - (matrix.Translation + matrix.Up * element.posUp));
                    var dirH = Vector3D.Normalize(headPos - (matrix.Translation + matrix.Left * element.posLeft));
                    
                    matrix.Translation += (matrix.Left * element.posLeft) + (matrix.Up * element.posUp);
                    
                    iconEntities[id].SetWorldMatrix(matrix);
                    
                    if(vectorGravity == null && vectorVelocity == null)
                        return;
                    
                    float angV = (float)Math.Acos(Math.Round(Vector3D.Dot(matrix.Backward, dirV), 4));
                    float angH = (float)Math.Acos(Math.Round(Vector3D.Dot(matrix.Backward, dirH), 4));
                    
                    if(element.posUp > 0)
                        angV = -angV;
                    
                    if(element.posLeft < 0)
                        angH = -angH;
                    
                    var offsetV = MatrixD.CreateFromAxisAngle(matrix.Left, angV);
                    var offsetH = MatrixD.CreateFromAxisAngle(matrix.Up, angH);
                    
                    if(vectorGravity != null)
                    {
                        var matrixGravity = matrix;
                        
                        AlignToVector(ref matrixGravity, gravityDir);
                        
                        matrixGravity *= offsetV * offsetH;
                        
                        var zScale = MathHelper.Clamp(gravityForce, 0, 3);
                        var xyScale = MathHelper.Clamp(zScale, 0.5, 1.0);
                        var vectorScale = new Vector3D(xyScale, xyScale, zScale);
                        MatrixD.Rescale(ref matrixGravity, ref vectorScale);
                        
                        matrixGravity.Translation = matrix.Translation;
                        
                        vectorGravity.SetWorldMatrix(matrixGravity);
                    }
                    
                    if(vectorVelocity != null)
                    {
                        var matrixVelocity = matrix;
                        
                        var num = 1.0f / velL;
                        var velN = new Vector3(vel.X * num, vel.Y * num, vel.Z * num);
                        
                        AlignToVector(ref matrixVelocity, velN);
                        
                        matrixVelocity *= offsetV * offsetH;
                        
                        var zScale = MathHelper.Clamp(velL / 50, 0, 3);
                        var xyScale = MathHelper.Clamp(zScale, 0.5, 1.0);
                        var vectorScale = new Vector3D(xyScale, xyScale, zScale);
                        MatrixD.Rescale(ref matrixVelocity, ref vectorScale);
                        
                        matrixVelocity.Translation = matrix.Translation;
                        
                        vectorVelocity.SetWorldMatrix(matrixVelocity);
                    }
                    
                    return;
                }
                
                // more curvature for the display
                if(id == Icons.DISPLAY)
                    headPos += matrix.Forward * 0.23;
                
                // horizon crosshair is always centered, no reason to apply TransformHUD()
                if(id == Icons.HORIZON)
                {
                    matrix.Translation += matrix.Forward * 0.02; // TODO fix the whiteness issue with horizon indicator and remove this afterwards
                }
                else
                {
                    matrix.Translation += (matrix.Left * element.posLeft) + (matrix.Up * element.posUp);
                    
                    // align the element to the view and give it the glass curve
                    TransformHUD(ref matrix, matrix.Translation + matrix.Forward * 0.05, headPos, -matrix.Up, matrix.Forward);
                }
                
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
                        
                        if(tick % SKIP_TICKS_HUD == 0)
                        {
                            if(settings.displayBorderColor.HasValue)
                            {
                                if(Vector3.DistanceSquared(lcdSlim.GetColorMask(), settings.displayBorderColor.Value) > 0.01f)
                                {
                                    ghostGrid.ColorBlocks(Vector3I.Zero, Vector3I.Zero, settings.displayBorderColor.Value);
                                    lastDisplayText = null; // force rewrite
                                }
                            }
                            else
                            {
                                var charColor = characterEntity.Render.ColorMaskHsv;
                                
                                if(Vector3.DistanceSquared(lcdSlim.GetColorMask(), charColor) > 0.01f)
                                {
                                    ghostGrid.ColorBlocks(Vector3I.Zero, Vector3I.Zero, charColor);
                                    lastDisplayText = null; // force rewrite
                                }
                            }
                        }
                        
                        str.Clear();
                        
                        if(MyHud.CharacterInfo.HealthRatio <= 0)
                        {
                            str.Append(DISPLAY_PAD).Append("ERROR #404:").AppendLine();
                            str.Append(DISPLAY_PAD).Append("USER LIFESIGNS NOT FOUND.").AppendLine();
                        }
                        else try
                        {
                            bool inShip = MyHud.ShipInfo.Visible;
                            float speed = 0;
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
                            string unit = "m/s";
                            
                            if(settings.displaySpeedUnit == SpeedUnits.kph)
                            {
                                speed *= 3.6f;
                                unit = "km/h";
                            }
                            
                            str.Append(DISPLAY_PAD).Append("Speed: ").Append(speed.ToString(FLOAT_FORMAT)).Append(unit).Append(" (").Append(accelSymbol).Append(accel.ToString(FLOAT_FORMAT)).Append(unit).Append(")").AppendLine();
                            
                            str.Append(DISPLAY_PAD).Append("Altitude: ");
                            
                            if(naturalForce > 0)
                                str.Append(altitude.ToString(FLOAT_FORMAT)).Append("m");
                            else
                                str.Append("N/A");
                            
                            str.AppendLine();
                            
                            str.Append(DISPLAY_PAD).Append("Gravity: ");
                            if(gravityForce > 0)
                                str.Append(Math.Round(naturalForce, 2)).Append("g natural, ").Append(Math.Round(artificialForce, 2)).Append("g artif.");
                            else
                                str.Append("None");
                            str.AppendLine();
                            
                            if(inShip)
                            {
                                var s = MyHud.ShipInfo;
                                str.Append(DISPLAY_PAD).Append("Ship mass: ");
                                
                                if(s.Mass > 1000000)
                                    str.Append((s.Mass / 100000f).ToString(FLOAT_FORMAT)).Append(" tonnes");
                                else
                                    str.Append(s.Mass.ToString(FLOAT_FORMAT)).Append(" kg");
                                
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
                            }
                            else
                            {
                                str.Append(DISPLAY_PAD).Append("Inventory: ").Append(Math.Round((float)MyHud.CharacterInfo.InventoryVolume * 1000, 2).ToString(FLOAT_FORMAT)).Append(" L (").Append(Math.Round(inventoryMass, 2).ToString(FLOAT_FORMAT)).Append(" kg)").AppendLine();
                                
                                float mass = MyHud.CharacterInfo.Mass;
                                
                                if(characterEntity != null && characterEntity.Physics != null)
                                    mass = characterEntity.Physics.Mass + inventoryMass;
                                
                                str.Append(DISPLAY_PAD).Append("Mass: ").Append(mass.ToString(FLOAT_FORMAT)).Append(" kg").AppendLine();
                                
                                str.Append(DISPLAY_PAD);
                                
                                if(MyAPIGateway.Session.CreativeMode)
                                {
                                    str.Append("No resource is being drained.");
                                }
                                else
                                {
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
                                }
                                
                                str.AppendLine();
                            }
                            
                            str.AppendLine();
                            
                            bool cubeBuilder = MyAPIGateway.CubeBuilder.IsActivated;
                            bool buildTool = false;
                            bool handDrill = false;
                            bool handWeapon = false;
                            
                            if(!cubeBuilder && !inShip && holdingTool != null)
                            {
                                if(holdingTool.Closed || holdingTool.MarkedForClose)
                                {
                                    holdingTool = null;
                                }
                                else
                                {
                                    if(holdingToolTypeId == typeof(MyObjectBuilder_Welder) || holdingToolTypeId == typeof(MyObjectBuilder_AngleGrinder))
                                        buildTool = true;
                                    else if(holdingToolTypeId == typeof(MyObjectBuilder_HandDrill))
                                        handDrill = true;
                                    else if(holdingToolTypeId == typeof(MyObjectBuilder_AutomaticRifle))
                                        handWeapon = true;
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
                                                str.Append(" +").Append(componentsCount - 3).Append(" other...").AppendLine();
                                            
                                            components.Clear();
                                        }
                                    }
                                }
                                else
                                {
                                    str.Append("Waiting for selection...").AppendLine();
                                }
                            }
                            else if(handDrill)
                            {
                                str.Append("Ore in range: ").Append(MyHud.OreMarkers.Count()).AppendLine();
                            }
                            else if(handWeapon)
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
                                            
                                            if(magName.Length > 22)
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
                                    var toolbarObj = (controlled as IMyCubeBlock).GetObjectBuilderCubeBlock(false) as MyObjectBuilder_ShipController;
                                    var grid = controlled.CubeGrid as IMyCubeGrid;
                                    
                                    if(toolbarObj.Toolbar != null && toolbarObj.Toolbar.SelectedSlot.HasValue)
                                    {
                                        var item = toolbarObj.Toolbar.Slots[toolbarObj.Toolbar.SelectedSlot.Value];
                                        
                                        if(item.Data is MyObjectBuilder_ToolbarItemWeapon)
                                        {
                                            var weapon = item.Data as MyObjectBuilder_ToolbarItemWeapon;
                                            bool shipWelder = weapon.DefinitionId.TypeId == typeof(MyObjectBuilder_ShipWelder);
                                            
                                            if(shipWelder || weapon.DefinitionId.TypeId == typeof(MyObjectBuilder_ShipGrinder))
                                            {
                                                blocks.Clear();
                                                grid.GetBlocks(blocks, b => b.FatBlock != null);
                                                
                                                float cargo = 0;
                                                float cargoMax = 0;
                                                float cargoMass = 0;
                                                int tools = 0;
                                                MyInventory inv;
                                                
                                                foreach(var slim in blocks)
                                                {
                                                    if(shipWelder ? slim.FatBlock is Ingame.IMyShipWelder : slim.FatBlock is Ingame.IMyShipGrinder)
                                                    {
                                                        tools++;
                                                        
                                                        if((slim.FatBlock as MyEntity).TryGetInventory(out inv))
                                                        {
                                                            cargo += (float)inv.CurrentVolume;
                                                            cargoMax += (float)inv.MaxVolume;
                                                            cargoMass += (float)inv.CurrentMass;
                                                        }
                                                    }
                                                }
                                                
                                                str.Append(tools).Append("x ").Append(shipWelder ? "welders" : "grinders").Append(": ").Append(Math.Round((cargo / cargoMax) * 100, 2)).Append("% (").Append(cargoMass.ToString(NUMBER_FORMAT)).Append(" kg)").AppendLine();
                                                
                                                var targetGrid = MyAPIGateway.CubeBuilder.FindClosestGrid();
                                                
                                                if(targetGrid != null)
                                                {
                                                    var view = MyAPIGateway.Session.ControlledObject.GetHeadMatrix(true, true);
                                                    var rayFrom = view.Translation + view.Forward * 2;
                                                    var rayTo = view.Translation + view.Forward * 10;
                                                    var blockPos = targetGrid.RayCastBlocks(rayFrom, rayTo);
                                                    
                                                    if(blockPos.HasValue)
                                                    {
                                                        var slimBlock = targetGrid.GetCubeBlock(blockPos.Value);
                                                        
                                                        if(slimBlock == null)
                                                        {
                                                            Log.Error("Unexpected empty block slot at " + blockPos.Value);
                                                            return;
                                                        }
                                                        
                                                        var block = slimBlock.FatBlock;
                                                        MyObjectBuilder_CubeBlock obj = null;
                                                        MyCubeBlockDefinition def;
                                                        MyDefinitionId defId;
                                                        
                                                        if(block == null)
                                                        {
                                                            obj = slimBlock.GetObjectBuilder();
                                                            defId = obj.GetId();
                                                        }
                                                        else
                                                        {
                                                            defId = block.BlockDefinition;
                                                        }
                                                        
                                                        if(MyDefinitionManager.Static.TryGetCubeBlockDefinition(defId, out def))
                                                        {
                                                            if(block is IMyTerminalBlock)
                                                                str.Append("\"").Append((block as IMyTerminalBlock).CustomName).Append("\"").AppendLine();
                                                            else
                                                                str.Append(def.DisplayNameText).AppendLine();
                                                            
                                                            str.Append("Integrity: "+Math.Round((slimBlock.BuildIntegrity / slimBlock.MaxIntegrity) * 100, 2)+"% ("+Math.Round(def.CriticalIntegrityRatio * 100, 0)+"% / "+Math.Round(def.OwnershipIntegrityRatio * 100, 0)+"%)").AppendLine();
                                                            
                                                            Dictionary<string, int> missing = new Dictionary<string, int>();
                                                            slimBlock.GetMissingComponents(missing);
                                                            int missingCount = missing.Count;
                                                            
                                                            if(missingCount > 0)
                                                            {
                                                                int max = 3;
                                                                int line = 0;
                                                                
                                                                foreach(var comp in missing)
                                                                {
                                                                    str.Append("  ").Append(comp.Value).Append("x ").Append(comp.Key).AppendLine();
                                                                    
                                                                    if(++line >= max)
                                                                        break;
                                                                }
                                                                
                                                                //if(missingCount > max)
                                                                //	str.Append(" +").Append(missingCount - 3).Append(" other...").AppendLine();
                                                            }
                                                            else
                                                            {
                                                                str.Append("  No missing components.").AppendLine();
                                                            }
                                                        }
                                                        else
                                                        {
                                                            str.Append("ERROR: No definition for block").AppendLine();
                                                        }
                                                    }
                                                    else
                                                    {
                                                        str.Append("Aim at a block to inspect it.").AppendLine();
                                                    }
                                                }
                                                else
                                                {
                                                    str.Append("Aim at a block to inspect it.").AppendLine();
                                                }
                                            }
                                            else if(weapon.DefinitionId.TypeId == typeof(MyObjectBuilder_SmallGatlingGun))
                                            {
                                                blocks.Clear();
                                                grid.GetBlocks(blocks, b => b.FatBlock != null);
                                                
                                                float gatlingAmmo = 0;
                                                float containerAmmo = 0;
                                                int gatlingGuns = 0;
                                                MyInventory inv;
                                                MyAmmoMagazineDefinition magDef = null;
                                                
                                                foreach(var slim in blocks)
                                                {
                                                    if(slim.FatBlock is Ingame.IMySmallGatlingGun)
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
                                                                magDef = MyDefinitionManager.Static.GetAmmoMagazineDefinition(magId);
                                                                
                                                                if((slim.FatBlock as MyEntity).TryGetInventory(out inv))
                                                                    gatlingAmmo += (int)inv.GetItemAmount(magDef.Id, MyItemFlags.None) * magDef.Capacity;
                                                                
                                                                break;
                                                            }
                                                        }
                                                    }
                                                }
                                                
                                                foreach(var slim in blocks)
                                                {
                                                    if(slim.FatBlock is Ingame.IMyCargoContainer)
                                                    {
                                                        if((slim.FatBlock as MyEntity).TryGetInventory(out inv))
                                                        {
                                                            containerAmmo += (float)inv.GetItemAmount(magDef.Id, MyItemFlags.None) * magDef.Capacity;
                                                        }
                                                    }
                                                }
                                                
                                                str.Append(gatlingGuns).Append("x gatling guns: ").Append((int)gatlingAmmo).AppendLine();
                                                str.Append("Ammo in containers: ").Append((int)containerAmmo).AppendLine();
                                            }
                                            else if(weapon.DefinitionId.TypeId == typeof(MyObjectBuilder_SmallMissileLauncher) || weapon.DefinitionId.TypeId == typeof(MyObjectBuilder_SmallMissileLauncherReload))
                                            {
                                                bool reloadable = weapon.DefinitionId.TypeId == typeof(MyObjectBuilder_SmallMissileLauncherReload);
                                                
                                                blocks.Clear();
                                                grid.GetBlocks(blocks, b => b.FatBlock != null);
                                                
                                                int launchers = 0;
                                                float launcherAmmo = 0;
                                                float containerAmmo = 0;
                                                MyInventory inv;
                                                MyAmmoMagazineDefinition magDef = null;
                                                
                                                foreach(var slim in blocks)
                                                {
                                                    if(slim.FatBlock is Ingame.IMySmallMissileLauncher && (reloadable == slim.FatBlock is Ingame.IMySmallMissileLauncherReload))
                                                    {
                                                        var obj = slim.FatBlock.GetObjectBuilderCubeBlock(false) as MyObjectBuilder_SmallMissileLauncher;
                                                        var def = MyDefinitionManager.Static.GetCubeBlockDefinition(slim.FatBlock.BlockDefinition) as MyWeaponBlockDefinition;
                                                        var wepDef = MyDefinitionManager.Static.GetWeaponDefinition(def.WeaponDefinitionId);
                                                        var currentMag = obj.GunBase.CurrentAmmoMagazineName;
                                                        var types = wepDef.AmmoMagazinesId.Length;
                                                        
                                                        launchers++;
                                                        launcherAmmo += obj.GunBase.RemainingAmmo;
                                                        
                                                        for(int i = 0; i < types; i++)
                                                        {
                                                            var magId = wepDef.AmmoMagazinesId[i];
                                                            
                                                            if(magId.SubtypeName == currentMag)
                                                            {
                                                                magDef = MyDefinitionManager.Static.GetAmmoMagazineDefinition(magId);
                                                                
                                                                if((slim.FatBlock as MyEntity).TryGetInventory(out inv))
                                                                    launcherAmmo += (float)inv.GetItemAmount(magDef.Id, MyItemFlags.None) * magDef.Capacity;
                                                                
                                                                break;
                                                            }
                                                        }
                                                    }
                                                }
                                                
                                                foreach(var slim in blocks)
                                                {
                                                    if(slim.FatBlock is Ingame.IMyCargoContainer)
                                                    {
                                                        if((slim.FatBlock as MyEntity).TryGetInventory(out inv))
                                                        {
                                                            containerAmmo += (float)inv.GetItemAmount(magDef.Id, MyItemFlags.None) * magDef.Capacity;
                                                        }
                                                    }
                                                }
                                                
                                                str.Append(launchers).Append("x missile launchers: ").Append((int)launcherAmmo).AppendLine();
                                                str.Append("Ammo in containers: ").Append((int)containerAmmo).AppendLine();
                                            }
                                            else if(weapon.DefinitionId.TypeId == typeof(MyObjectBuilder_Drill))
                                            {
                                                blocks.Clear();
                                                grid.GetBlocks(blocks, b => b.FatBlock != null);
                                                
                                                float cargo = 0;
                                                float cargoMax = 0;
                                                float cargoMass = 0;
                                                int containers = 0;
                                                float drillVol = 0;
                                                float drillVolMax = 0;
                                                float drillMass = 0;
                                                int drills = 0;
                                                MyInventory inv;
                                                
                                                foreach(var slim in blocks)
                                                {
                                                    if(slim.FatBlock is Ingame.IMyShipDrill)
                                                    {
                                                        drills++;
                                                        
                                                        if((slim.FatBlock as MyEntity).TryGetInventory(out inv))
                                                        {
                                                            drillVol += (float)inv.CurrentVolume;
                                                            drillVolMax += (float)inv.MaxVolume;
                                                            drillMass += (float)inv.CurrentMass;
                                                        }
                                                    }
                                                    else if(slim.FatBlock is Ingame.IMyCargoContainer)
                                                    {
                                                        containers++;
                                                        
                                                        if((slim.FatBlock as MyEntity).TryGetInventory(out inv))
                                                        {
                                                            cargo += (float)inv.CurrentVolume;
                                                            cargoMax += (float)inv.MaxVolume;
                                                            cargoMass += (float)inv.CurrentMass;
                                                        }
                                                    }
                                                }
                                                
                                                str.Append(drills).Append("x drills: ").Append(Math.Round((drillVol / drillVolMax) * 100, 2)).Append("% (").Append(drillMass.ToString(NUMBER_FORMAT)).Append(" kg)").AppendLine();
                                                str.Append(containers).Append("x containers: ").Append(Math.Round((cargo / cargoMax) * 100, 2)).Append("% (").Append(cargoMass.ToString(NUMBER_FORMAT)).Append(" kg)").AppendLine();
                                            }
                                        }
                                    }
                                    else
                                    {
                                        var blockName = controlled.CustomName.ToLower();
                                        
                                        if(blockName.Contains("ores"))
                                        {
                                            str.Append("Ore in range: ").Append(MyHud.OreMarkers.Count()).AppendLine();
                                        }
                                        
                                        bool showGatling = false; //blockName.Contains("gatling");
                                        bool showLaunchers = false; //blockName.Contains("launchers");
                                        bool showAmmo = false; //blockName.Contains("ammo");
                                        bool showCargo = blockName.Contains("cargo");
                                        bool showOxygen = blockName.Contains("oxygen");
                                        bool showHydrogen = blockName.Contains("hydrogen");
                                        
                                        if(showGatling || showLaunchers || showAmmo || showCargo || showOxygen || showHydrogen)
                                        {
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
                                                    str.Append(containers).Append("x Container: ").Append(Math.Round((cargo / totalCargo) * 100f, 2)).Append("% (").Append(cargoMass.ToString(NUMBER_FORMAT)).Append("kg)");
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
                        }
                        catch(Exception e)
                        {
                            str.Append("ERROR, SEND LOG TO AUTHOR.").AppendLine();
                            Log.Error(e);
                        }
                        
                        displayText = str.ToString();
                        str.Clear();
                        
                        if(lastDisplayText == null || !displayText.Equals(lastDisplayText))
                        {
                            displayUpdate = true;
                            lastDisplayText = displayText;
                            
                            // updating somewhere else due to a recently appeared issue
                            //lcd.WritePublicText(text);
                            //lcd.ShowTextureOnScreen();
                            //lcd.ShowPublicTextOnScreen();
                        }
                    }
                }
                
                if(!element.hasBar)
                    return; // if the HUD element has no bar, stop here.
                
                if(iconBarEntities[id] == null)
                {
                    iconBarEntities[id] = SpawnPrefab(settings.GetRenderPrefix() + CUBE_HUD_PREFIX + element.name + "Bar");
                    
                    if(iconBarEntities[id] == null)
                        return;
                }
                
                if(id == Icons.HORIZON)
                {
                    var controller = MyAPIGateway.Session.ControlledObject as MyShipController;
                    
                    var dotV = Vector3.Dot(naturalDir, controller.WorldMatrix.Forward);
                    var dotH = Vector3.Dot(naturalDir, controller.WorldMatrix.Right);
                    
                    matrix.Translation += matrix.Up * (dotV * 0.035);
                    
                    var tmp = matrix.Translation;
                    matrix *= MatrixD.CreateFromAxisAngle(matrix.Backward, MathHelper.ToRadians(dotH * 90));
                    matrix.Translation = tmp;
                    
                    // horizon bar is not a resizable or blinking bar so just update it here and stop
                    iconBarEntities[id].SetWorldMatrix(matrix);
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
                PrefabBuilder.CubeBlocks.Clear(); // need no leftovers from previous spawns
                
                if(isDisplay)
                {
                    Vector3 borderColor = Vector3.Zero;
                    
                    if(settings.displayBorderColor.HasValue)
                        borderColor = settings.displayBorderColor.Value;
                    else if(characterEntity != null)
                        borderColor = characterEntity.Render.ColorMaskHsv;
                    
                    PrefabBuilder.CubeBlocks.Add(new MyObjectBuilder_TextPanel()
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
                                                     ColorMaskHSV = borderColor,
                                                 });
                    
                    PrefabBuilder.CubeBlocks.Add(PrefabBattery);
                }
                else
                {
                    PrefabCubeBlock.SubtypeName = name;
                    PrefabBuilder.CubeBlocks.Add(PrefabCubeBlock);
                }
                
                PrefabBuilder.DisplayName = PrefabBuilder.Name = "helmetmod_helmet_"+(MyAPIGateway.Multiplayer.IsServer ? "server" : "client")+"_"+name;
                
                MyAPIGateway.Entities.RemapObjectBuilder(PrefabBuilder);
                var ent = MyAPIGateway.Entities.CreateFromObjectBuilder(PrefabBuilder);
                ent.Flags &= ~EntityFlags.Sync; // don't sync on MP
                ent.Flags &= ~EntityFlags.Save; // don't save this entity
                ent.PersistentFlags &= ~MyPersistentEntityFlags2.CastShadows;
                ent.CastShadows = false;
                
                foreach(var c in ent.Hierarchy.Children)
                {
                    c.Entity.PersistentFlags &= ~MyPersistentEntityFlags2.CastShadows;
                    c.Entity.Render.CastShadows = false;
                }
                
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
            CubeBlocks = new List<MyObjectBuilder_CubeBlock>(),
        };
        private static MyObjectBuilder_CubeBlock PrefabCubeBlock = new MyObjectBuilder_CubeBlock()
        {
            EntityId = 1,
            SubtypeName = "",
            Min = PrefabVectorI0,
            BlockOrientation = PrefabOrientation,
            DeformationRatio = 0,
            ShareMode = MyOwnershipShareModeEnum.None,
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

        public void CalculateGravityAndPlanetsAt(Vector3D point)
        {
            artificialDir = Vector3.Zero;
            naturalDir = Vector3.Zero;
            gravityForce = 0;
            artificialForce = 0;
            naturalForce = 0;
            altitude = 0;
            
            try
            {
                foreach(var kv in planets)
                {
                    var planet = kv.Value;
                    
                    if(planet.Closed || planet.MarkedForClose)
                        continue;
                    
                    var dir = planet.PositionComp.GetPosition() - point;
                    
                    if(dir.LengthSquared() <= planet.GravityLimitSq)
                    {
                        altitude = (float)Vector3D.Distance(point, planet.GetClosestSurfacePointGlobal(ref point));
                        dir.Normalize();
                        naturalDir += dir * planet.GetGravityMultiplier(point);
                    }
                }
                
                naturalForce = naturalDir.Length();
                
                foreach(var generator in gravityGenerators.Values)
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
                                artificialDir += (Vector3)dir * (gen.Gravity / 9.81f); // HACK remove division once gravity value is fixed
                            }
                        }
                        else if(generator is IMyGravityGenerator)
                        {
                            var gen = (generator as IMyGravityGenerator);
                            
                            var halfExtents = new Vector3(gen.FieldWidth / 2, gen.FieldHeight / 2, gen.FieldDepth / 2);
                            var box = new MyOrientedBoundingBoxD(gen.WorldMatrix.Translation, halfExtents, Quaternion.CreateFromRotationMatrix(gen.WorldMatrix));
                            
                            if(box.Contains(ref point))
                            {
                                artificialDir += gen.WorldMatrix.Down * gen.Gravity;
                            }
                        }
                    }
                }
                
                float mul = MathHelper.Clamp(1f - naturalForce * 2f, 0f, 1f);
                artificialDir *= mul;
                artificialForce = artificialDir.Length();
                gravityDir = (naturalDir + artificialDir);
                gravityForce = gravityDir.Length();
                
                // normalize gravityDir
                float num = 1.0f / gravityForce;
                gravityDir.X *= num;
                gravityDir.Y *= num;
                gravityDir.Z *= num;
                
                gravityForce = (float)Math.Round(gravityForce, 2);
                naturalForce = (float)Math.Round(naturalForce, 2);
                artificialForce = (float)Math.Round(artificialForce, 2);
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
                    settings.renderer = MyGraphicsRenderer.DX9;
                    settings.Save();
                    RemoveHelmet();
                    return;
                }
                else if(msg.StartsWith("dx11"))
                {
                    MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Configured for DX11; saved to config.");
                    settings.renderer = MyGraphicsRenderer.DX11;
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
                else if(msg.StartsWith("lcd off"))
                {
                    MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "LCD turned OFF; saved to config.");
                    settings.elements[Icons.DISPLAY].show = 0;
                    settings.Save();
                    RemoveHelmet();
                    return;
                }
                else if(msg.StartsWith("lcd on"))
                {
                    MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "LCD turned ON; saved to config.");
                    settings.elements[Icons.DISPLAY].show = 3;
                    settings.Save();
                    RemoveHelmet();
                    return;
                }
            }
            
            MyAPIGateway.Utilities.ShowMissionScreen("Helmet Mod Commands", "", "You can type these commands in the chat.", HELP_COMMANDS, null, "Close");
        }
    }
    
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_TextPanel), "HelmetHUD_display", "HelmetHUD_displayLow")] // LCD blinking workaround
    public class HelmetLCD : MyGameLogicComponent
    {
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
        }
        
        public override void UpdateAfterSimulation()
        {
            if(Helmet.displayUpdate)
            {
                Helmet.displayUpdate = false;
                var panel = Entity as IMyTextPanel;
                panel.ShowTextureOnScreen();
                panel.WritePublicText(Helmet.displayText, false);
                panel.ShowPublicTextOnScreen();
            }
        }
        
        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return Entity.GetObjectBuilder(copy);
        }
    }
    
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_GravityGenerator))]
    public class GravityGeneratorFlat : GravityGeneratorLogic { }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_GravityGeneratorSphere))]
    public class GravityGeneratorSphere : GravityGeneratorLogic { }

    public class GravityGeneratorLogic : MyGameLogicComponent
    {
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            Entity.NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }
        
        public override void UpdateOnceBeforeFrame()
        {
            try
            {
                var block = Entity as IMyGravityGeneratorBase;
                
                if(block.CubeGrid.Physics == null)
                    return;
                
                Helmet.gravityGenerators.Add(block.EntityId, block);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        public override void Close()
        {
            try
            {
                Helmet.gravityGenerators.Remove(Entity.EntityId);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return Entity.GetObjectBuilder(copy);
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