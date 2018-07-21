using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Lights;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces; // needed for TerminalPropertyExtensions
using Sandbox.ModAPI.Weapons;
using SpaceEngineers.Game.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Components.Session;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Input;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

using IMyControllableEntity = Sandbox.Game.Entities.IMyControllableEntity;

namespace Digi.Helmet
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class Helmet : MySessionComponentBase
    {
        public override void LoadData()
        {
            Log.SetUp("Helmet", 428842256);
        }

        private const string MOD_DEV_NAME = "Helmet.dev";
        private const int WORKSHOP_DEV_ID = 429601133;

        public bool init { get; private set; }
        public bool isDedicatedHost { get; private set; }
        public Settings settings { get; private set; }

        public static Helmet instance { get; private set; }

        public string pathToMod = "";

        // DEBUG 'helmet' entity removed
        //private IMyEntity helmet = null;
        private bool drawVisor = false;

        private IMyEntity ghostLCD = null; // shows HUD marker info centered; this is not the bottom display
        public IMyCharacter characterEntity = null;
        private MyCharacterDefinition characterDefinition = null;
        private bool characterHasHelmet = true;
        private bool removedHUD = false;
        private bool warningBlinkOn = false;
        private double lastWarningBlink = 0;
        private long helmetBroken = 0;
        private bool prevHelmetOn = false;
        private long animationStart = 0;
        private bool drawHelmetAndHUD = false;
        private bool helmetOn = false;
        private bool inCockpit = false;
        private bool markerBlinkMemory = false;
        private bool vanillaHUD = true;
        private bool quickToggleMarkers = true;

        public readonly static Dictionary<long, IMyGravityProvider> gravityGenerators = new Dictionary<long, IMyGravityProvider>();

        public const float G_ACCELERATION = 9.81f;

        private Vector3 artificialDir = Vector3.Zero;
        private Vector3 naturalDir = Vector3.Zero;
        private Vector3 gravityDir = Vector3.Zero;
        private float artificialForce = 0;
        private float naturalForce = 0;
        private float gravityForce = 0;
        private float altitude = 0;
        private float inventoryMass = 0;

        private float oldFov = 70;

        public short tick = 0;
        public const int SKIP_TICKS_FOV = 60;
        public const int SKIP_TICKS_HUD = 5;
        public const int SKIP_TICKS_GRAVITY = 2;
        public const int SKIP_TICKS_PLANETS = 60 * 30;
        public const int SKIP_TICKS_MARKERS = 30;

        public class HudElementData
        {
            public bool show = false;
            public int showIcon = 0;
            public float value = 0;
            public float prevValue = 0;
            public long lastChangedTime = 0;
        }

        private readonly HudElementData[] hudElementData = new HudElementData[Settings.TOTAL_ELEMENTS];

        private MatrixD headMatrix;
        private MatrixD helmetMatrix;
        private MatrixD hudMatrix;
        /*
        private readonly float[] values = new float[Settings.TOTAL_ELEMENTS];
        private readonly float[] prevValues = new float[Settings.TOTAL_ELEMENTS];
        private readonly long[] lastChanged = new long[Settings.TOTAL_ELEMENTS];
        private readonly int[] showIcons = new int[Settings.TOTAL_ELEMENTS];
        */
        private MatrixD prevHeadMatrix;
        private int lastOxygenEnv = 0;
        private bool fatalError = false;

        public static IMyEntity holdingTool = null;
        public static MyObjectBuilderType holdingToolTypeId;

        public readonly IMyEntity[] iconEntities = new IMyEntity[Settings.TOTAL_ELEMENTS];
        public readonly IMyEntity[] iconBarEntities = new IMyEntity[Settings.TOTAL_ELEMENTS];

        private double lastLinearSpeed;
        private Vector3D lastDirSpeed;

        public string lastDisplayText = null;
        private short skipDisplay = 0;
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

        private const string DISPLAY_PAD = " ";
        private const float DISPLAY_FONT_SIZE = 1.3f;
        private const string NUMBER_FORMAT = "###,###,##0";
        private const string FLOAT_FORMAT = "###,###,##0.##";

        private const float HUD_COLOR_SCALE = 0.65f;
        private static readonly Color HUD_RED = new Color((int)(255 * HUD_COLOR_SCALE), 0, 0);
        private static readonly Color HUD_ORANGE = new Color((int)(255 * HUD_COLOR_SCALE), (int)(175 * HUD_COLOR_SCALE), 0);
        private static readonly Color HUD_BLUE = new Color(0, (int)(148 * HUD_COLOR_SCALE), (int)(255 * HUD_COLOR_SCALE));
        private static readonly Color HUD_PURPLE = new Color((int)(178 * HUD_COLOR_SCALE), 0, (int)(255 * HUD_COLOR_SCALE));
        private static readonly Color HUD_GREEN = new Color(0, (int)(255 * HUD_COLOR_SCALE), (int)(33 * HUD_COLOR_SCALE));
        private static readonly Color HUD_GRAY = new Color((int)(128 * HUD_COLOR_SCALE), (int)(128 * HUD_COLOR_SCALE), (int)(128 * HUD_COLOR_SCALE));
        private static readonly Color HUD_DARKGRAY = new Color((int)(64 * HUD_COLOR_SCALE), (int)(64 * HUD_COLOR_SCALE), (int)(64 * HUD_COLOR_SCALE));
        private static readonly MyStringId MATERIAL_HUD = MyStringId.GetOrCompute("SquareIgnoreDepth");
        private static readonly MyStringId MATERIAL_BAR_WARNING_BG = MyStringId.GetOrCompute("HelmetHUDBackground_Warning");

        private const float MARKER_SIZE = 0.0002f;

        private readonly List<IMyGps> hudGPSMarkers = new List<IMyGps>();
        private readonly Dictionary<IMyEntity, HelmetHudMarker> hudEntityMarkers = new Dictionary<IMyEntity, HelmetHudMarker>();
        public readonly SortedList<float, string> selectedSort = new SortedList<float, string>(new DuplicateKeyComparer<float>());
        public static int hudMarkers = 0;
        public static float hudMarkersFromDist = 0;

        public const int MARKERS_MAX_SELECTED = 3;

        public struct HelmetHudMarker
        {
            public readonly string name;
            public readonly Color color;
            public readonly float size;

            public HelmetHudMarker(string name, Color color, float size)
            {
                this.name = name;
                this.color = color;
                this.size = size;
            }
        }

        public readonly List<MyPlanet> planets = new List<MyPlanet>();
        private readonly static HashSet<IMyEntity> ents = new HashSet<IMyEntity>(); // this is always empty

        private Dictionary<string, Vector3> lightOffsetDefault = new Dictionary<string, Vector3>();

        private static readonly int[] sphereQualityIndex = { 0, 8, 12, 20, 24 };
        private static readonly int[] arrowQualityIndex = { 3, 8, 12, 16, 20 };

        private readonly Random rand = new Random();
        private readonly Dictionary<string, int> components = new Dictionary<string, int>();
        private readonly List<IMySlimBlock> blocks = new List<IMySlimBlock>();
        private readonly List<IMyTerminalBlock> terminalBlocks = new List<IMyTerminalBlock>();
        private readonly List<IMyTerminalBlock> terminalBlocksEmpty = new List<IMyTerminalBlock>(0); // always empty
        private readonly StringBuilder str = new StringBuilder();
        private readonly StringBuilder tmp = new StringBuilder();

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

        public static readonly MyDefinitionId DEFID_OXYGEN = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Oxygen");
        public static readonly MyDefinitionId DEFID_HYDROGEN = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Hydrogen");

        public static readonly MyObjectBuilder_AmmoMagazine AMMO_BULLETS = new MyObjectBuilder_AmmoMagazine() { SubtypeName = "NATO_25x184mm" };
        public static readonly MyObjectBuilder_AmmoMagazine AMMO_MISSILES = new MyObjectBuilder_AmmoMagazine() { SubtypeName = "Missile200mm" };

        private const string HELP_COMMANDS =
            "/helmet for fov [number]   number is optional, quickly set scales for a specified FOV value\n" +
            "/helmet <on/off>   turn the entire mod on or off\n" +
            "/helmet scale <number>   -1.0 to 1.0, default 0\n" +
            "/helmet hud <on/off>   turn the HUD component on or off\n" +
            "/helmet hud scale <number>   -1.0 to 1.0, default 0\n" +
            "/helmet reload   re-loads the config file (for advanced editing)\n" +
            "/helmet glass <on/off>   turn glass reflections on or off\n" +
            "/helmet lcd <on/off>   turns the LCD on or off\n" +
            "\n" +
            "For advanced editing go to:\n" +
            "%appdata%\\SpaceEngineers\\Storage\\428842256_Helmet\\helmet.cfg";

        public void Init()
        {
            Log.Init();

            instance = this;
            init = true;
            isDedicatedHost = (MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Multiplayer.IsServer);

            if(!isDedicatedHost)
            {
                settings = new Settings();

                MyAPIGateway.Utilities.MessageEntered += MessageEntered;
                MyAPIGateway.Session.DamageSystem.RegisterDestroyHandler(999, EntityKilled);

                for(int id = 0; id < Settings.TOTAL_ELEMENTS; id++)
                {
                    hudElementData[id] = new HudElementData();
                }

                // TODO feature: lights
                //var mods = MyAPIGateway.Session.Mods;
                //bool found = false;
                //
                //foreach(var mod in mods)
                //{
                //    if(mod.PublishedFileId == 0 ? mod.Name == MOD_DEV_NAME : (mod.PublishedFileId == Log.WORKSHOP_ID || mod.PublishedFileId == WORKSHOP_DEV_ID))
                //    {
                //        pathToMod = MyAPIGateway.Utilities.GamePaths.ModsPath + @"\" + mod.Name + @"\";
                //        found = true;
                //        break;
                //    }
                //}
                //
                //if(found)
                //    Log.Info("Helmet Mod: PathToMod set to: " + pathToMod);
                //else
                //    Log.Error("Helmet Mod: Can't find mod " + Log.WORKSHOP_ID + ".sbm or " + MOD_DEV_NAME + " in the mod list!");
                //
                //lightOffsetDefault.Clear();
                //
                //foreach(var def in MyDefinitionManager.Static.Characters)
                //{
                //    lightOffsetDefault.Add(def.Id.SubtypeName, def.LightOffset);
                //}
                //
                //SetCharacterLightOffsets(settings.lightReplace > 0);
            }
        }

        protected override void UnloadData()
        {
            try
            {
                instance = null;

                if(init)
                {
                    init = false;
                    ents.Clear();
                    planets.Clear();
                    gravityGenerators.Clear();
                    holdingTool = null;

                    if(!isDedicatedHost)
                    {
                        MyAPIGateway.Utilities.MessageEntered -= MessageEntered;

                        if(settings != null)
                        {
                            settings.Close();
                            settings = null;
                        }
                    }

                    // TODO feature: lights
                    //lightOffsetDefault.Clear();
                    //UpdateLight(0, remove: true);
                    //UpdateLight(1, remove: true);
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            Log.Close();
        }

        // TODO feature: lights
        //private void SetCharacterLightOffsets(bool custom)
        //{
        //    if(lightOffsetDefault.Count == 0)
        //        return;
        //
        //    foreach(var def in MyDefinitionManager.Static.Characters)
        //    {
        //        def.LightOffset = (custom ? LIGHT_OFFSET_CUSTOM : lightOffsetDefault.GetValueOrDefault(def.Id.SubtypeName, Vector3.Zero));
        //    }
        //}

        public void EntityKilled(object obj, MyDamageInformation info)
        {
            try
            {
                if(characterEntity == null)
                    return;

                var ent = obj as IMyCharacter;

                if(ent != null && characterEntity.EntityId == ent.EntityId && glassBreakCauses.Contains(info.Type))
                {
                    helmetBroken = ent.EntityId;
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        //private Benchmark bench_update = new Benchmark("update");

        public override void UpdateAfterSimulation()
        {
            //bench_update.Start();

            try
            {
                if(drawHelmetAndHUD)
                    drawHelmetAndHUD = false;

                if(isDedicatedHost)
                    return;

                if(fatalError)
                    return; // stop trying if a fatal error occurs

                if(!init)
                {
                    if(MyAPIGateway.Session == null)
                        return;

                    Init();

                    if(isDedicatedHost)
                        return;
                }

                if(!settings.enabled)
                {
                    RemoveHelmet(true);
                    return;
                }

                tick++; // global update tick

                // required here because it gets the previous value if used in HandleInput()
                if(MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.TOGGLE_HUD))
                    vanillaHUD = !MyAPIGateway.Session.Config.MinimalHud;

                HelmetLogicReturn ret = UpdateHelmetLogic();

                if(ret == HelmetLogicReturn.OK)
                {
                    drawHelmetAndHUD = true;
                }
                else
                {
                    RemoveHelmet(ret == HelmetLogicReturn.REMOVE_ALL);
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            //bench_update.End();
        }

        private enum HelmetLogicReturn
        {
            OK,
            REMOVE_ONLY_VIGNETTE,
            REMOVE_ALL,
        }

        private HashSet<MyDataBroadcaster> radioBroadcasters = new HashSet<MyDataBroadcaster>();
        private HashSet<long> tmpEntitiesOnHUD = new HashSet<long>();
        private List<IMyTerminalBlock> dummyTerminalList = new List<IMyTerminalBlock>(0); // always empty

        // HACK: copied from MyAntennaSystem.GetAllRelayedBroadcasters(MyDataReceiver receiver, ...)
        private void GetAllRelayedBroadcasters(MyDataReceiver receiver, long identityId, bool mutual, HashSet<MyDataBroadcaster> output = null)
        {
            if(output == null)
            {
                output = radioBroadcasters;
                output.Clear();
            }

            foreach(MyDataBroadcaster current in receiver.BroadcastersInRange)
            {
                if(!output.Contains(current) && !current.Closed && (!mutual || (current.Receiver != null && receiver.Broadcaster != null && current.Receiver.BroadcastersInRange.Contains(receiver.Broadcaster))))
                {
                    output.Add(current);

                    if(current.Receiver != null && current.CanBeUsedByPlayer(identityId))
                    {
                        GetAllRelayedBroadcasters(current.Receiver, identityId, mutual, output);
                    }
                }
            }
        }

        private HelmetLogicReturn UpdateHelmetLogic()
        {
            var camera = MyAPIGateway.Session.CameraController;

            if(camera == null || MyAPIGateway.Session.ControlledObject == null || MyAPIGateway.Session.ControlledObject.Entity == null)
                return HelmetLogicReturn.REMOVE_ALL;

            if(characterEntity != null && (characterEntity.MarkedForClose || characterEntity.Closed))
            {
                UpdateCharacterReference(null);
            }

            var controlled = MyAPIGateway.Session.ControlledObject.Entity;

            if(camera is IMyCharacter)
            {
                UpdateCharacterReference((IMyCharacter)camera);
            }
            else if(controlled is MyShipController && camera is MyShipController)
            {
                var shipController = controlled as MyShipController;
                UpdateCharacterReference(shipController.Pilot);
            }
            else
            {
                return HelmetLogicReturn.REMOVE_ALL;
            }

            if(characterEntity != null)
            {
                if(!characterHasHelmet) // prevent crashes when using helmets on dogs/spiders/etc that have no OxygenComponent, would crash on respawn.
                {
                    return HelmetLogicReturn.REMOVE_ALL; ;
                }

                helmetOn = (characterEntity as IMyControllableEntity).EnabledHelmet;

                if(helmetOn != prevHelmetOn)
                {
                    prevHelmetOn = helmetOn;

                    if(settings.animateTime > 0)
                        animationStart = DateTime.UtcNow.Ticks; // set start of animation here to avoid animating after going in first person
                    else if(!helmetOn)
                        return HelmetLogicReturn.REMOVE_ONLY_VIGNETTE;
                }
            }
            else
            {
                return HelmetLogicReturn.REMOVE_ALL;
            }

            if(!camera.IsInFirstPersonView || !(characterEntity is IMyControllableEntity))
                return HelmetLogicReturn.REMOVE_ALL;

            if(settings.autoFovScale)
            {
                if(tick % SKIP_TICKS_FOV == 0)
                {
                    float fov = MathHelper.ToDegrees(MyAPIGateway.Session.Config.FieldOfView);

                    if(Math.Abs(oldFov - fov) > float.Epsilon)
                    {
                        settings.ScaleForFOV(fov);
                        settings.Save();
                        oldFov = fov;
                    }
                }
            }

            inCockpit = (MyAPIGateway.Session.CameraController is IMyCockpit);
            bool brokenHelmet = (helmetBroken > 0 && helmetBroken == characterEntity.EntityId);

            if(helmetOn)
                drawVisor = true;

#if false // DEBUG 'helmet' entity removed
            if(helmetOn && brokenHelmet)
            {
                helmetBroken = 0;
                RemoveHelmet(removeHud: false); // don't return, only refresh helmet mesh
            }
            
            if(helmetOn)
            {
                // Spawn the helmet model if it's not spawned
                if(brokenHelmet || (helmet == null && settings.helmetModel != null))
                {
                    helmet = SpawnPrefab(CUBE_HELMET_PREFIX + (brokenHelmet ? CUBE_HELMET_BROKEN_SUFFIX : settings.GetHelmetModel()));
            
                    if(helmet == null)
                    {
                        Log.Error("Couldn't load the helmet prefab!");
                        fatalError = true;
                        return HelmetLogicReturn.REMOVE_ALL;
                    }
                }
            }
#endif

            if(settings.hud)
            {
                var controllableEntity = (controlled as IMyControllableEntity);

                if(!vanillaHUD && !MyAPIGateway.Gui.IsCursorVisible && !MyAPIGateway.Gui.ChatEntryVisible && MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.TOGGLE_SIGNALS) && !MyAPIGateway.Input.IsAnyCtrlKeyPressed())
                {
                    quickToggleMarkers = !quickToggleMarkers;
                }

                if(tick % SKIP_TICKS_HUD == 0)
                {
                    // show and value cache for the HUD elements
                    for(int id = 0; id < Settings.TOTAL_ELEMENTS; id++)
                    {
                        var elementData = hudElementData[id];

                        elementData.prevValue = elementData.value;
                        elementData.value = 0;

                        int show = settings.elements[id].show;

                        if(show != 0 && settings.hudAlways)
                            show = 3;

                        elementData.showIcon = show;
                    }

                    bool inShip = controlled is IMyShipController;

                    hudElementData[Icons.HEALTH].value = characterEntity.Integrity;
                    hudElementData[Icons.ENERGY].value = characterEntity.SuitEnergyLevel * 100;
                    hudElementData[Icons.HYDROGEN].value = characterEntity.GetSuitGasFillLevel(DEFID_HYDROGEN) * 100;
                    hudElementData[Icons.OXYGEN].value = characterEntity.GetSuitGasFillLevel(DEFID_OXYGEN) * 100;
                    hudElementData[Icons.OXYGEN_ENV].value = characterEntity.EnvironmentOxygenLevel * 2;
                    hudElementData[Icons.BROADCASTING].value = (inShip || controllableEntity.EnabledBroadcasting ? 1 : 0);
                    hudElementData[Icons.DAMPENERS].value = (controllableEntity.EnabledDamping ? 1 : 0);
                    hudElementData[Icons.THRUSTERS].value = (controllableEntity.EnabledThrusts ? 1 : 0);
                    hudElementData[Icons.LIGHTS].value = (controllableEntity.EnabledLights ? 1 : 0);
                    hudElementData[Icons.INVENTORY].value = 0;
                    inventoryMass = 0;

                    var inv = characterEntity.GetInventory();

                    if(inv != null)
                    {
                        hudElementData[Icons.INVENTORY].value = ((float)inv.CurrentVolume / (float)inv.MaxVolume) * 100;
                        inventoryMass = (float)inv.CurrentMass;
                    }

                    for(int id = 0; id < Settings.TOTAL_ELEMENTS; id++)
                    {
                        var elementData = hudElementData[id];

                        if(Math.Abs(elementData.prevValue - elementData.value) > 0.01f)
                        {
                            elementData.lastChangedTime = DateTime.UtcNow.Ticks;
                        }
                    }
                }

                // Update the warning icon
                hudElementData[Icons.WARNING].showIcon = 0;

                int moveMode = (controllableEntity.EnabledThrusts ? 2 : 1);

                if(settings.elements[Icons.WARNING].show > 0)
                {
                    for(int id = 0; id < Settings.TOTAL_ELEMENTS; id++)
                    {
                        if(settings.elements[id].warnPercent >= 0 && hudElementData[id].value <= settings.elements[id].warnPercent)
                        {
                            if(settings.elements[id].warnMoveMode == 0 || settings.elements[id].warnMoveMode == moveMode)
                            {
                                double warnTick = DateTime.UtcNow.Ticks;

                                if(lastWarningBlink < warnTick)
                                {
                                    warningBlinkOn = !warningBlinkOn;
                                    lastWarningBlink = warnTick + (TimeSpan.TicksPerSecond * settings.warnBlinkTime);
                                }

                                hudElementData[Icons.WARNING].showIcon = 1;
                                break;
                            }
                        }
                    }
                }

                if(!quickToggleMarkers)
                    hudElementData[Icons.MARKERS].showIcon = 0;

                if(hudElementData[Icons.MARKERS].showIcon > 0)
                {
                    if(tick % SKIP_TICKS_MARKERS == 0)
                    {
                        hudGPSMarkers.Clear();

                        if(settings.markerShowGPS)
                            MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId, hudGPSMarkers);

                        hudEntityMarkers.Clear();

                        if(settings.markerShowAntennas || settings.markerShowBeacons || settings.markerShowBlocks)
                        {
                            var charRadio = characterEntity.Components.Get<MyDataReceiver>();
                            var charOrCockpit = (controlled is IMyCharacter || controlled is IMyCockpit);
                            var identityId = MyAPIGateway.Session.Player.IdentityId;

                            GetAllRelayedBroadcasters(charRadio, identityId, false, null);

                            foreach(MyDataBroadcaster current in radioBroadcasters)
                            {
                                var ent = current.Entity as MyEntity;

                                if(ent == null)
                                    continue;

                                if(charOrCockpit && ent == characterEntity)
                                    continue; // do not show myself

                                var size = MARKER_SIZE;
                                var chr = ent as IMyCharacter;
                                Color color;

                                if(chr != null)
                                {
                                    if(hudEntityMarkers.ContainsKey(chr))
                                        continue;

                                    var relation = MyAPIGateway.Session.Player.GetRelationsBetweenPlayers(chr.ControllerInfo.ControllingIdentityId);
                                    color = settings.markerColorNeutral;

                                    switch(relation)
                                    {
                                        case MyRelationsBetweenPlayers.Self:
                                        case MyRelationsBetweenPlayers.Allies:
                                            color = settings.markerColorFaction;
                                            break;
                                        case MyRelationsBetweenPlayers.Enemies:
                                            color = settings.markerColorEnemy;
                                            break;
                                    }

                                    hudEntityMarkers.Add(chr, new HelmetHudMarker(chr.DisplayName, color, size / 2));
                                    continue;
                                }

                                var grid = ent.GetTopMostParent(null) as MyCubeGrid;

                                if(grid == null || grid.IsPreview)
                                    continue;

                                if(hudEntityMarkers.ContainsKey(ent))
                                    continue;

                                MyIDModule idModule;
                                var compId = ent as IMyComponentOwner<MyIDModule>;
                                color = settings.markerColorNeutral;
                                string name = grid.DisplayName;

                                if(compId != null && compId.GetComponent(out idModule))
                                {
                                    switch(idModule.GetUserRelationToOwner(MyAPIGateway.Session.Player.IdentityId))
                                    {
                                        case MyRelationsBetweenPlayerAndBlock.Enemies:
                                            color = settings.markerColorEnemy;
                                            break;
                                        case MyRelationsBetweenPlayerAndBlock.FactionShare:
                                            color = settings.markerColorFaction;
                                            break;
                                        case MyRelationsBetweenPlayerAndBlock.Owner:
                                            color = settings.markerColorOwned;
                                            break;
                                    }
                                }

                                var la = ent as IMyLaserAntenna;

                                if(la != null && (!la.ShowOnHUD || settings.markerShowBlocks))
                                    continue;

                                var cube = ent as IMyCubeBlock;

                                if(cube != null)
                                {
                                    if(cube.CubeGrid.GridSizeEnum == MyCubeSize.Large)
                                        size *= 2.5f;

                                    if(cube.IsBeingHacked)
                                    {
                                        if(tick % 15 == 0)
                                            markerBlinkMemory = !markerBlinkMemory;

                                        if(markerBlinkMemory)
                                            continue;
                                    }

                                    var terminal = cube as IMyTerminalBlock;
                                    name = (terminal != null ? terminal.CustomName : cube.DefinitionDisplayNameText);
                                }

                                var ant = ent as IMyRadioAntenna;

                                if(ant != null)
                                {
                                    if(!ant.IsWorking)
                                        continue;

                                    if(ant.HasLocalPlayerAccess())
                                    {
                                        var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(ant.CubeGrid as IMyCubeGrid);
                                        //var yourFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(MyAPIGateway.Session.Player.IdentityId);

                                        terminalSystem.GetBlocksOfType<IMyTerminalBlock>(terminalBlocksEmpty, delegate (IMyTerminalBlock b)
                                                                                         {
                                                                                             if(b == ant || !b.HasLocalPlayerAccess())
                                                                                                 return false;

                                                                                             if(b.ShowOnHUD
                                                                                                || (b.IsBeingHacked && b.OwnerId != 0)
                                                                                                || (b is IMyCockpit && (b as MyCockpit).Pilot != null))
                                                                                             {
                                                                                                 if(hudEntityMarkers.ContainsKey(b))
                                                                                                     return false;

                                                                                                 string n = null;
                                                                                                 var cockpit = b as MyCockpit;

                                                                                                 if(cockpit != null && cockpit.Pilot != null)
                                                                                                 {
                                                                                                     var pilot = (IMyCharacter)cockpit.Pilot;

                                                                                                     if(pilot.ControllerInfo != null)
                                                                                                     {
                                                                                                         var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(pilot.ControllerInfo.ControllingIdentityId);

                                                                                                         // TODO relation color?
                                                                                                         //MyAPIGateway.Session.Factions.GetRelationBetweenFactions(faction.FactionId, 

                                                                                                         if(faction != null)
                                                                                                             n = faction.Tag + ". " + pilot.DisplayName;
                                                                                                     }

                                                                                                     if(n == null)
                                                                                                         n = pilot.DisplayName;
                                                                                                 }
                                                                                                 else
                                                                                                     n = b.CustomName;

                                                                                                 hudEntityMarkers.Add(b, new HelmetHudMarker(n, settings.markerColorBlock, size / 2));
                                                                                             }

                                                                                             return false;
                                                                                         });
                                    }
                                }

                                hudEntityMarkers.Add(ent, new HelmetHudMarker(name, color, size));
                            }
                        }
                    }
                }

                if(hudElementData[Icons.VECTOR].showIcon > 0 || hudElementData[Icons.DISPLAY].showIcon > 0 || hudElementData[Icons.HORIZON].showIcon > 0)
                {
                    if(tick % SKIP_TICKS_PLANETS == 0)
                    {
                        planets.Clear();
                        MyAPIGateway.Entities.GetEntities(ents, delegate (IMyEntity e)
                                                          {
                                                              var p = e as MyPlanet;

                                                              if(p != null)
                                                                  planets.Add(p);

                                                              return false; // no reason to add to the list
                                                          });
                    }

                    if(tick % SKIP_TICKS_GRAVITY == 0)
                    {
                        CalculateGravityAndPlanetsAt(characterEntity.WorldAABB.Center);
                    }

                    var shipController = MyAPIGateway.Session.ControlledObject as MyShipController;
                    hudElementData[Icons.HORIZON].showIcon = ((naturalForce > 0 && shipController != null && shipController.HorizonIndicatorEnabled) ? 1 : 0);
                }
            }
            else
            {
                RemoveHud();
            }

            return HelmetLogicReturn.OK;
        }

        private void UpdateCharacterReference(IMyCharacter charEnt)
        {
            characterEntity = charEnt;
            characterDefinition = null;
            characterHasHelmet = false;

            // charEnt.ToString() returns character's model name
            if(charEnt != null && MyDefinitionManager.Static.Characters.TryGetValue(charEnt.ToString(), out characterDefinition))
                characterHasHelmet = (characterDefinition.SuitResourceStorage != null && characterDefinition.SuitResourceStorage.Count > 0);
        }

        private void RemoveHelmet(bool removeHud = true)
        {
            drawVisor = false;
            animationStart = 0;

#if false // DEBUG 'helmet' entity removed
            if(!removedHelmet)
            {
                removedHelmet = true;

                if(helmet != null)
                {
                    helmet.Visible = false;
                    helmet.Close();
                    helmet = null;
                }
            }
#endif

            if(removeHud && !removedHUD)
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
                    iconEntities[id].Visible = false;
                    iconEntities[id].Close();
                    iconEntities[id] = null;
                }

                if(iconBarEntities[id] != null)
                {
                    iconBarEntities[id].Visible = false;
                    iconBarEntities[id].Close();
                    iconBarEntities[id] = null;
                }

                if(id == Icons.MARKERS)
                {
                    if(ghostLCD != null)
                    {
                        ghostLCD.Visible = false;
                        ghostLCD.Close();
                        ghostLCD = null;
                    }
                }
            }

            lastDisplayText = null;
        }

        // TODO feature: lights
        /*
        public class Particle
        {
            public Vector3 position;
            public Vector3 velocity;
            public float radius;
            public float angle;
            public float alpha;

            private readonly int hashCode = 0;
            private static int uniqueHashCode = 0;

            public Particle()
            {
                unchecked
                {
                    uniqueHashCode++;
                    hashCode = uniqueHashCode;
                }
            }

            public override int GetHashCode()
            {
                return hashCode;
            }

            public override bool Equals(object obj)
            {
                var other = obj as Particle;

                if(other == null)
                    return false;

                return this.hashCode == other.hashCode;
            }

            public static bool operator ==(Particle l, Particle r)
            {
                if(ReferenceEquals(l, r))
                    return true;

                if(ReferenceEquals(l, null) || ReferenceEquals(r, null))
                    return false;

                return l.Equals(r);
            }

            public static bool operator !=(Particle l, Particle r)
            {
                return !(l == r);
            }
        }
        
        private float lightBeamIntensity = 1;
        private float airDensity = 0;

        private readonly HashSet<Particle> dustParticles = new HashSet<Particle>();
        private readonly HashSet<Particle> removedustParticles = new HashSet<Particle>();
        private float speedAlpha = 0f;
        private MyLight[] lights = new MyLight[2];
        
        public class CharacterLightData
        {
            public readonly IMyCharacter character;
            public MyLight[] lights = new MyLight[2];

            public CharacterLightData(IMyCharacter character)
            {
                this.character = character;
            }
        }

        public static readonly Dictionary<long, CharacterLightData> characterEntities = new Dictionary<long, CharacterLightData>();

        public static readonly Color DUST_PARTICLE_COLOR = Color.Wheat;
        
        private static readonly Vector3 LIGHT_OFFSET_CUSTOM = new Vector3(0, 0, float.MaxValue);

        private const float CONE_LIGHT_RANGE = 120f;
        private const float CONE_DEGREES = 70f;
        private const float CONE_END_OFFSET = 15;
        private const float CONE_BASE_RADIUS = 56; // for 120 length and 70deg
        private const float CONE_DUST_LENGTH = 6;

        private void GetLightConeData(bool left, float range, float sideOffset, out Vector3D tip, out Vector3D end, out Vector3D dir)
        {
            var side = (left ? headMatrix.Left : headMatrix.Right);
            var center = headMatrix.Translation + headMatrix.Up * 0.15 + headMatrix.Forward * 0.1;
            tip = center + side * 0.2;
            end = center + side * sideOffset + headMatrix.Forward * range;
            dir = Vector3D.Normalize(end - tip);
        }

        // rethink for usage in all characters
        private void UpdateLight(int index, bool on = false, bool remove = false)
        {
            var light = lights[index];

            if(remove)
            {
                if(light != null)
                {
                    MyLights.RemoveLight(light);
                    lights[index] = null;
                }

                return;
            }

            Vector3D coneTip, coneEnd, coneDir;
            GetLightConeData(index == 0, CONE_LIGHT_RANGE, CONE_END_OFFSET, out coneTip, out coneEnd, out coneDir);

            if(on)
            {
                if(light == null)
                {
                    light = MyLights.AddLight();
                    light.Start(MyLight.LightTypeEnum.Spotlight, 1f);
                    light.LightOwner = MyLight.LightOwnerEnum.SmallShip;
                    light.ReflectorTexture = pathToMod + @"Textures\Light.dds";
                    light.UseInForwardRender = true;
                    light.ReflectorConeDegrees = CONE_DEGREES;
                    light.ReflectorColor = Color.White;
                    light.ReflectorIntensity = 3f;
                    light.ReflectorFalloff = 5f;
                    light.ReflectorRange = CONE_LIGHT_RANGE;
                    light.ShadowDistance = light.ReflectorRange;
                    light.CastShadows = true;

                    lights[index] = light;
                }

                if(!light.LightOn)
                {
                    light.LightOn = true;
                    light.ReflectorOn = true;
                }

                light.Position = coneTip;
                light.ReflectorDirection = coneDir;
                light.ReflectorUp = headMatrix.Up;

                if(!drawHelmet)
                {
                    var skinned = characterEntity as MySkinnedEntity;
                    int headBone;

                    if(skinned.AnimationController.FindBone("SE_RigHead", out headBone) != null)
                    {
                        var charMatrix = characterEntity.WorldMatrix;
                        var boneMatrix = skinned.BoneAbsoluteTransforms[headBone];

                        light.Position = charMatrix.Translation + Vector3D.TransformNormal(boneMatrix.Translation, charMatrix);
                        light.Position += (index == 0 ? charMatrix.Left : charMatrix.Right) * 0.2;

                        light.ReflectorDirection = Vector3D.TransformNormal(boneMatrix.Up, charMatrix);

                        // project and remove the left/right direction
                        light.ReflectorDirection = Vector3D.Normalize(light.ReflectorDirection - (charMatrix.Right * charMatrix.Right.Dot(light.ReflectorDirection)));
                        light.ReflectorUp = Vector3D.Cross(light.ReflectorDirection, charMatrix.Left);
                    }
                    else
                    {
                        // fallback in case we can't find the head bone for some reason
                        var charCtrl = MyAPIGateway.Session.ControlledObject as IMyControllableEntity;
                        var matrix = charCtrl.GetHeadMatrix(false, false, true, true);
                        light.Position = matrix.Translation + (index == 0 ? matrix.Left : matrix.Right) * 0.2;
                        light.ReflectorUp = matrix.Up;
                        light.ReflectorDirection = matrix.Forward;
                    }
                }

                light.UpdateLight();
            }
            else
            {
                if(light != null && light.LightOn)
                {
                    light.LightOn = false;
                    light.ReflectorOn = false;
                    light.UpdateLight();
                }
            }

            if(on && drawHelmet && settings.lightBeams > 0 && lightBeamIntensity > 0)
            {
                const float coneEndOffset = 0.5f * CONE_END_OFFSET;

                GetLightConeData(index == 0, CONE_LIGHT_RANGE, coneEndOffset, out coneTip, out coneEnd, out coneDir);

                coneTip += headMatrix.Backward * 0.1; // offset cone tip to not see the light beam start sprite as a circle

                float step = 0.001f;
                float alpha = 0.03f;

                const float stepMul = 1.333f;
                const float alphaMul = 0.95f;
                const float maxRadius = 135;
                const float alphaLimit = 0.018f;

                while(step < 1)
                {
                    var finalAlpha = alpha * lightBeamIntensity;

                    if(finalAlpha <= alphaLimit)
                        break;

                    var pos = Vector3D.Lerp(coneTip, coneEnd, step);
                    var radius = MathHelper.Lerp(0, maxRadius, step);

                    MyTransparentGeometry.AddPointBillboard("LightBeamPart", Color.White * finalAlpha, pos, radius, 0);

                    step *= stepMul;
                    alpha *= alphaMul;
                }
            }
        }
        
        private bool IsInCone(Vector3D checkPos, Vector3D coneTip, Vector3D coneDir, float coneLength, float coneRadius, out float edgeAlpha)
        {
            var diff = checkPos - coneTip;
            var coneDist = Vector3D.Dot(diff, coneDir);
            edgeAlpha = 1;

            if(coneDist < 0 || coneDist > coneLength)
                return false;

            var radSq = (coneDist / coneLength) * coneRadius;
            radSq *= radSq;
            var orthDistSq = (diff - coneDist * coneDir).LengthSquared();

            if(orthDistSq < radSq)
            {
                const double ALPHA_DIST_RATIO = 0.5;
                const double MUL = 1 / ALPHA_DIST_RATIO;

                edgeAlpha = (float)(Math.Min(1.0 - (orthDistSq / radSq), ALPHA_DIST_RATIO) * MUL);
                return true;
            }

            return false;
        }
        */

        public override void Draw()
        {
            try
            {
                if(!init || !settings.enabled || isDedicatedHost)
                    return;

                headMatrix = MyAPIGateway.Session.Camera.WorldMatrix;
                SmoothHeadMatrix();

                if(drawHelmetAndHUD)
                    DrawHelmet();

                // TODO feature: lights
                /*
                if(characterEntity == null)
                    return;
                
                // light beams should mind the world's oxygen setting

                if(settings.lightReplace > 0)
                {
                    bool lightsOn = characterEntity.EnabledLights;

                    if(settings.lightBeams == 2)
                    {
                        if(tick % 60 == 0)
                        {
                            airDensity = characterEntity.EnvironmentOxygenLevel;

                            if(airDensity <= 0) // if there's no oxygen around, check if there's an atmosphere
                            {
                                var pos = characterEntity.WorldMatrix.Translation;
                                int planetNum = 0;

                                foreach(var kv in planets)
                                {
                                    var planet = kv.Value;

                                    if(planet == null || planet.Closed)
                                        continue;

                                    airDensity += planet.GetAirDensity(pos);
                                    planetNum++;
                                }

                                if(airDensity > 0)
                                    airDensity /= planetNum;

                                // HACK scenario not accounted for: on a planet but inside a depressurized ship
                            }
                        }

                        var atmosphereDiff = airDensity - lightBeamIntensity;
                        var atmosphereDiffAbs = Math.Abs(atmosphereDiff);

                        if(atmosphereDiffAbs > float.Epsilon)
                        {
                            float step = Math.Max(atmosphereDiffAbs / 60f, 0.000001f);

                            if(lightBeamIntensity > airDensity)
                                lightBeamIntensity = Math.Max(lightBeamIntensity - step, airDensity);
                            else
                                lightBeamIntensity = Math.Min(lightBeamIntensity + step, airDensity);
                        }
                    }
                    else if(settings.lightBeams == 1)
                    {
                        lightBeamIntensity = 1;
                    }

                    UpdateLight(0, lightsOn);
                    UpdateLight(1, lightsOn);

                    if(drawHelmet && lightsOn && settings.lightDustParticles > 0 && lightBeamIntensity > 0)
                    {
                        const float ignoreUpToSpeedSq = 7 * 7;
                        const float visibleMaxSpeedSq = 3 * 3;
                        const float alphaStep = 0.008f;
                        float speedSq = Math.Max(characterEntity.Physics.LinearVelocity.LengthSquared() - ignoreUpToSpeedSq, 0);
                        float targetAlpha = 1f - (Math.Min(speedSq, visibleMaxSpeedSq) / visibleMaxSpeedSq);

                        if(speedAlpha > targetAlpha)
                            speedAlpha = Math.Max(speedAlpha - alphaStep, targetAlpha);
                        else if(speedAlpha < targetAlpha)
                            speedAlpha = Math.Min(speedAlpha + alphaStep, targetAlpha);

                        if(speedAlpha <= 0)
                        {
                            if(dustParticles.Count > 0)
                            {
                                dustParticles.Clear();
                                removedustParticles.Clear();
                            }
                        }
                        else
                        {
                            const float lengthRatio = (CONE_DUST_LENGTH / CONE_LIGHT_RANGE);
                            const float coneBaseRadius = lengthRatio * CONE_BASE_RADIUS;
                            const float coneEndOffset = lengthRatio * CONE_END_OFFSET;

                            Vector3D coneTipLeft, coneEndLeft, coneDirLeft;
                            Vector3D coneTipRight, coneEndRight, coneDirRight;
                            GetLightConeData(true, CONE_DUST_LENGTH, coneEndOffset, out coneTipLeft, out coneEndLeft, out coneDirLeft);
                            GetLightConeData(false, CONE_DUST_LENGTH, coneEndOffset, out coneTipRight, out coneEndRight, out coneDirRight);
                            
                            //{
                            //    var m = MatrixD.CreateWorld(coneTipLeft, coneDirLeft, headMatrix.Up);
                            //    var c = Color.Red * 0.1f;
                            //    MySimpleObjectDraw.DrawTransparentCone(ref m, coneBaseRadius, CONE_LENGTH, ref c, 20, "Square");
                            //}

                            int maxParticles = (settings.lightDustParticles == 1 ? 15 : 30);
                            const float stepSize = 0.1f;
                            const float startStep = 0.03f;
                            float step = startStep;

                            while(dustParticles.Count < maxParticles)
                            {
                                if(step > 1)
                                    step = startStep;

                                step += stepSize;

                                if(rand.Next(100) > 25)
                                    continue;

                                var start = (rand.Next(2) == 0 ? coneTipLeft : coneTipRight);
                                var dir = (rand.Next(2) == 0 ? coneDirLeft : coneDirRight);

                                var radius = step * coneBaseRadius;
                                var theta = rand.NextDouble() * Math.PI * 2;
                                var distance = Math.Sqrt(rand.NextDouble()) * radius;
                                var x = distance * Math.Cos(theta);
                                var y = distance * Math.Sin(theta);

                                dustParticles.Add(new Particle()
                                {
                                    position = start + (dir * step * CONE_DUST_LENGTH) + (headMatrix.Left * x) + (headMatrix.Up * y),
                                    velocity = 0.002f * rand.Next(11) * new Vector3(rand.Next(-2, 2), rand.Next(-2, 2), rand.Next(-2, 2)),
                                    radius = Math.Max(0.013f * (float)rand.NextDouble(), 0.002f),
                                    angle = rand.Next(-90, 90),
                                    alpha = 0.23f + ((float)rand.NextDouble() * 0.03f),
                                });
                            }

                            if(dustParticles.Count > 0)
                            {
                                var particleIndex = 0;

                                foreach(var p in dustParticles)
                                {
                                    if(++particleIndex > maxParticles)
                                    {
                                        removedustParticles.Add(p);
                                        continue;
                                    }

                                    float edgeAlpha1 = 1;
                                    float edgeAlpha2 = 1;

                                    if(!IsInCone(p.position, coneTipLeft, coneDirLeft, CONE_DUST_LENGTH, coneBaseRadius, out edgeAlpha1)
                                        && !IsInCone(p.position, coneTipRight, coneDirRight, CONE_DUST_LENGTH, coneBaseRadius, out edgeAlpha2))
                                    {
                                        removedustParticles.Add(p);
                                        continue;
                                    }

                                    var distSq = (float)Vector3D.DistanceSquared(headMatrix.Translation, p.position);
                                    var halfLengthSq = (CONE_DUST_LENGTH / 2);
                                    halfLengthSq *= halfLengthSq;
                                    float distAlpha = 1f;
                                    const float minDistSq = (0.3f * 0.3f);

                                    if(distSq > halfLengthSq)
                                        distAlpha = distSq / (CONE_DUST_LENGTH * CONE_DUST_LENGTH);
                                    else if(distSq < minDistSq)
                                        distAlpha = distSq / minDistSq;

                                    MyTransparentGeometry.AddPointBillboard("Stardust", DUST_PARTICLE_COLOR * p.alpha * lightBeamIntensity * distAlpha * speedAlpha * edgeAlpha1 * edgeAlpha2, p.position, p.radius, p.angle);

                                    if(p.angle > 0)
                                        p.angle += 0.001f;
                                    else
                                        p.angle -= 0.001f;

                                    p.position += p.velocity / 60.0f;
                                }

                                if(removedustParticles.Count > 0)
                                {
                                    foreach(var p in removedustParticles)
                                    {
                                        dustParticles.Remove(p);
                                    }

                                    removedustParticles.Clear();
                                }
                            }
                        }
                    }

                    var camPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation;
                    const int CHARACTER_LIGHT_LOD_DIST_SQ = 500;

                    foreach(var charLight in characterEntities.Values)
                    {
                        if(charLight.character.EnabledLights)
                        {
                            var m = charLight.character.WorldMatrix;
                            var head = m.Translation + m.Up * 1.6;

                            MyTransparentGeometry.AddLineBillboard("Square", Color.Red, head, m.Forward, 10, 0.05f); // DEBUG
                        }
                    }
                }
                else
                {
                    foreach(var charLight in characterEntities.Values)
                    {
                        charLight.lights[0] = null;
                        charLight.lights[1] = null;
                    }

                    UpdateLight(0, remove: true);
                    UpdateLight(1, remove: true);
                }
                */
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        private void SmoothHeadMatrix()
        {
            if(settings.delayedRotation > 0)
            {
                double lenDiff = (prevHeadMatrix.Forward - headMatrix.Forward).Length() * 4 * settings.delayedRotation;
                double amount = MathHelper.Clamp(lenDiff, 0.25, 1.0);

                double linearSpeed = (prevHeadMatrix.Translation - headMatrix.Translation).Length();
                double linearAccel = lastLinearSpeed - linearSpeed;

                Vector3D dirSpeed = (prevHeadMatrix.Translation - headMatrix.Translation);
                Vector3D dirAccel = (lastDirSpeed - dirSpeed);

                double accel = dirAccel.Length();

                lastDirSpeed = dirSpeed;
                lastLinearSpeed = linearSpeed;

                const double minMax = 0.005;
                double posAmount = MathHelper.Clamp(accel / 10, 0.01, 1.0);

                headMatrix.M11 = prevHeadMatrix.M11 + (headMatrix.M11 - prevHeadMatrix.M11) * amount;
                headMatrix.M12 = prevHeadMatrix.M12 + (headMatrix.M12 - prevHeadMatrix.M12) * amount;
                headMatrix.M13 = prevHeadMatrix.M13 + (headMatrix.M13 - prevHeadMatrix.M13) * amount;
                headMatrix.M14 = prevHeadMatrix.M14 + (headMatrix.M14 - prevHeadMatrix.M14) * amount;
                headMatrix.M21 = prevHeadMatrix.M21 + (headMatrix.M21 - prevHeadMatrix.M21) * amount;
                headMatrix.M22 = prevHeadMatrix.M22 + (headMatrix.M22 - prevHeadMatrix.M22) * amount;
                headMatrix.M23 = prevHeadMatrix.M23 + (headMatrix.M23 - prevHeadMatrix.M23) * amount;
                headMatrix.M24 = prevHeadMatrix.M24 + (headMatrix.M24 - prevHeadMatrix.M24) * amount;
                headMatrix.M31 = prevHeadMatrix.M31 + (headMatrix.M31 - prevHeadMatrix.M31) * amount;
                headMatrix.M32 = prevHeadMatrix.M32 + (headMatrix.M32 - prevHeadMatrix.M32) * amount;
                headMatrix.M33 = prevHeadMatrix.M33 + (headMatrix.M33 - prevHeadMatrix.M33) * amount;
                headMatrix.M34 = prevHeadMatrix.M34 + (headMatrix.M34 - prevHeadMatrix.M34) * amount;

                headMatrix.M41 = headMatrix.M41 + MathHelper.Clamp((prevHeadMatrix.M41 - headMatrix.M41) * posAmount, -minMax, minMax);
                headMatrix.M42 = headMatrix.M42 + MathHelper.Clamp((prevHeadMatrix.M42 - headMatrix.M42) * posAmount, -minMax, minMax);
                headMatrix.M43 = headMatrix.M43 + MathHelper.Clamp((prevHeadMatrix.M43 - headMatrix.M43) * posAmount, -minMax, minMax);

                headMatrix.M44 = prevHeadMatrix.M44 + (headMatrix.M44 - prevHeadMatrix.M44) * amount;
                prevHeadMatrix = headMatrix;
            }
        }

        // HACK temporary
        private readonly MyStringId MATERIAL_VIGNETTE = MyStringId.GetOrCompute("HelmetVignette");
        private readonly MyStringId MATERIAL_VIGNETTE_SIDES = MyStringId.GetOrCompute("HelmetVignetteSides");
        private readonly MyStringId MATERIAL_VIGNETTE_NOREFLECTION = MyStringId.GetOrCompute("HelmetVignette_NoReflection");
        private readonly MyStringId MATERIAL_VIGNETTE_BROKEN = MyStringId.GetOrCompute("HelmetVignette_Broken");

        private void DrawHelmet()
        {
            // Align the helmet mesh

#if false // DEBUG 'helmet' entity removed
            if(helmet != null)
#endif
            if(drawVisor)
            {
                helmetMatrix = headMatrix;

                // altered for the sprite technique
                helmetMatrix.Translation += headMatrix.Forward * SCALE_DIST_ADJUST;
                //helmetMatrix.Translation += headMatrix.Forward * (SCALE_DIST_ADJUST * (1.0 - settings.scale));

                if(animationStart > 0)
                {
                    float tickDiff = (float)MathHelper.Clamp((DateTime.UtcNow.Ticks - animationStart) / (TimeSpan.TicksPerMillisecond * settings.animateTime * 1000), 0, 1.1f);

                    if(tickDiff >= 1)
                    {
                        animationStart = 0;

                        if(!helmetOn)
                            RemoveHelmet(false);

                        return;
                    }

                    var toMatrix = MatrixD.CreateWorld(helmetMatrix.Translation + helmetMatrix.Up * 0.5, helmetMatrix.Up, helmetMatrix.Backward);
                    helmetMatrix = (helmetOn ? MatrixD.Slerp(toMatrix, helmetMatrix, tickDiff) : MatrixD.Slerp(helmetMatrix, toMatrix, tickDiff));
                }

                // HACK temporary solution on the visor interaction issue
                float height = 2.25f * SCALE_DIST_ADJUST; // eyeballed this value
                float width = height * 1.45f; // calculated from the model height/width ratio

                var cam = MyAPIGateway.Session.Camera;
                var scaleFOV = (float)Math.Tan(MyAPIGateway.Session.Camera.FovWithZoom / 2);
                var scaleVisor = (float)(scaleFOV * settings.visorScale);
                width *= scaleVisor;
                height *= scaleVisor;

                var p = helmetMatrix.Translation;
                MyQuadD quad;
                MyUtils.GenerateQuad(out quad, ref p, width, height, ref helmetMatrix);
                uint parentId = 0; // characterEntity.Render.GetRenderObjectID();
                bool brokenHelmet = (helmetBroken > 0 && helmetBroken == characterEntity.EntityId);
                var helmetMaterial = (brokenHelmet ? MATERIAL_VIGNETTE_BROKEN : (settings.glassReflections ? MATERIAL_VIGNETTE : MATERIAL_VIGNETTE_NOREFLECTION));

                {
                    var p0 = quad.Point0;
                    var p1 = quad.Point1;
                    var p2 = quad.Point2;
                    var n0 = helmetMatrix.Forward;
                    var n1 = helmetMatrix.Forward;
                    var n2 = helmetMatrix.Forward;
                    var uv1 = new Vector2(0, 0);
                    var uv2 = new Vector2(0, 1);
                    var uv3 = new Vector2(1, 1);
                    MyTransparentGeometry.AddTriangleBillboard(p0, p1, p2, n0, n1, n2, uv1, uv2, uv3, helmetMaterial, parentId, p);
                }
                {
                    var p0 = quad.Point0;
                    var p1 = quad.Point3;
                    var p2 = quad.Point2;
                    var n0 = helmetMatrix.Forward;
                    var n1 = helmetMatrix.Forward;
                    var n2 = helmetMatrix.Forward;
                    var uv1 = new Vector2(0, 0);
                    var uv2 = new Vector2(1, 0);
                    var uv3 = new Vector2(1, 1);
                    MyTransparentGeometry.AddTriangleBillboard(p0, p1, p2, n0, n1, n2, uv1, uv2, uv3, helmetMaterial, parentId, p);
                }

                var sideP = p + helmetMatrix.Backward * height;
                MyTransparentGeometry.AddBillboardOriented(MATERIAL_VIGNETTE_SIDES, Color.Black, sideP + helmetMatrix.Left * width, helmetMatrix.Backward, helmetMatrix.Up, height, height);
                MyTransparentGeometry.AddBillboardOriented(MATERIAL_VIGNETTE_SIDES, Color.Black, sideP + helmetMatrix.Right * width, helmetMatrix.Forward, helmetMatrix.Up, height, height);
                MyTransparentGeometry.AddBillboardOriented(MATERIAL_VIGNETTE_SIDES, Color.Black, sideP + helmetMatrix.Up * height, helmetMatrix.Left, helmetMatrix.Forward, width, height);
                MyTransparentGeometry.AddBillboardOriented(MATERIAL_VIGNETTE_SIDES, Color.Black, sideP + helmetMatrix.Down * height, helmetMatrix.Left, helmetMatrix.Backward, width, height);

                //helmet.SetWorldMatrix(helmetMatrix);
                //removedHelmet = false;
            }

            // if HUD is disabled or we can't get the info, remove the HUD (if exists) and stop here
            if(!settings.hud)
                return;

            hudMatrix = headMatrix;
            hudMatrix.Translation += hudMatrix.Forward * (HUDSCALE_DIST_ADJUST * (1.0 - settings.hudScale)); // off-set the HUD according to the HUD scale
            var headPos = hudMatrix.Translation + (hudMatrix.Backward * 0.25); // for glass curve effect
            removedHUD = false; // mark the HUD as not removed because we're about to spawn it

            // spawn and update the HUD elements
            for(int id = 0; id < Settings.TOTAL_ELEMENTS; id++)
            {
                bool showIcon = false;

                switch(hudElementData[id].showIcon)
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
                            showIcon = vanillaHUD;
                            break;
                        case 2:
                            showIcon = !vanillaHUD;
                            break;
                    }
                }

                hudElementData[id].show = showIcon;
                DrawElement(headPos, id, showIcon);
            }
        }

        private void DrawElement(Vector3D headPos, int id, bool show)
        {
            var elementData = hudElementData[id];
            var elementSettings = settings.elements[id];
            var matrix = this.hudMatrix;

            // helmet on/off animation for everything except display, also ignores always shown elements
            if(show && id != Icons.DISPLAY && animationStart > 0 && !(settings.hudAlways || elementData.showIcon == 3))
            {
                float tickDiff = (float)Math.Min((DateTime.UtcNow.Ticks - animationStart) / (TimeSpan.TicksPerMillisecond * settings.animateTime * 1000), 1);
                var toMatrix = MatrixD.CreateWorld(matrix.Translation + matrix.Up * 0.5, matrix.Up, matrix.Backward);
                matrix = (characterEntity.EnabledHelmet ? MatrixD.Slerp(toMatrix, matrix, tickDiff) : MatrixD.Slerp(matrix, toMatrix, tickDiff));
            }

            switch(id)
            {
                case Icons.DAMPENERS:
                case Icons.BROADCASTING:
                case Icons.THRUSTERS:
                case Icons.LIGHTS:
                case Icons.WARNING:
                    {
                        if(!show)
                        {
                            if(id == Icons.WARNING)
                                warningBlinkOn = false; // reset the blink status for the warning icon

                            return;
                        }

                        // place the element on its position
                        matrix.Translation += (matrix.Left * elementSettings.posLeft) + (matrix.Up * elementSettings.posUp);

                        // align the element to the view and give it the glass curve
                        TransformHUD(ref matrix, matrix.Translation + matrix.Forward * 0.05, headPos, -matrix.Up, matrix.Forward);

                        const float alpha = 0.5f;

                        switch(id)
                        {
                            case Icons.DAMPENERS:
                            case Icons.BROADCASTING:
                            case Icons.THRUSTERS:
                            case Icons.LIGHTS:
                                var color = (elementData.value > 0 ? settings.statusIconOnColor : settings.statusIconOffColor);
                                long fadeTime = TimeSpan.TicksPerMillisecond * 1000;

                                if(elementData.lastChangedTime > 0)
                                {
                                    var diff = (DateTime.UtcNow.Ticks - elementData.lastChangedTime);

                                    if(diff < fadeTime)
                                    {
                                        float fadePercent = (float)diff / (float)fadeTime;
                                        color = Color.Lerp((elementData.value > 0 ? settings.statusIconSetOnColor : settings.statusIconSetOffColor), color, (fadePercent * fadePercent));
                                    }
                                    else
                                        elementData.lastChangedTime = 0;
                                }

                                color *= alpha;
                                Extensions.TempAddBillboardOriented(settings.defaultElements[id].material, color, matrix.Translation, matrix, 0.0025f);
                                break;
                            case Icons.WARNING:
                                if(show)
                                {
                                    if(warningBlinkOn)
                                        Extensions.TempAddBillboardOriented(settings.defaultElements[id].material, Color.White * alpha, matrix.Translation, matrix, 0.0075f);
                                }
                                else
                                {
                                    warningBlinkOn = false; // reset the blink status for the warning icon
                                }
                                break;
                        }

                        return;
                    }
            }

            // if the element should be hidden instead of shown, this executes then entire method ends
            if(!show)
            {
                // remove the element itself if it exists
                if(iconEntities[id] != null)
                {
                    iconEntities[id].Visible = false;
                    iconEntities[id].Close();
                    iconEntities[id] = null;
                }

                if(id == Icons.WARNING)
                    warningBlinkOn = false; // reset the blink status for the warning icon

                // remove the bar if it has one and it exists
                if(elementSettings.hasBar && iconBarEntities[id] != null)
                {
                    iconBarEntities[id].Visible = false;
                    iconBarEntities[id].Close();
                    iconBarEntities[id] = null;
                }

                if(id == Icons.MARKERS)
                {
                    if(ghostLCD != null)
                        ghostLCD.Visible = false; // hide the ghost LCD
                }

                return;
            }

            if(id == Icons.CROSSHAIR)
            {
                var cameraMatrix = MyAPIGateway.Session.Camera.WorldMatrix;
                var posHUD = matrix.Translation + matrix.Forward * 0.1 + matrix.Backward * (HUDSCALE_DIST_ADJUST * (1.0 - settings.hudScale)); // remove the HUD scale from the position
                var posRgid = cameraMatrix.Translation + cameraMatrix.Forward * 0.1;
                var pos = (animationStart > 0 ? posHUD : Vector3D.Lerp(posRgid, posHUD, settings.crosshairSwayRatio));

                Extensions.TempAddBillboardOriented(settings.crosshairTypeId, settings.crosshairColor, pos, matrix, settings.crosshairScale / 100f);
                return;
            }

            if(id == Icons.MARKERS)
            {
                if((hudGPSMarkers.Count == 0 && hudEntityMarkers.Count == 0) || animationStart > 0)
                {
                    if(ghostLCD != null)
                        ghostLCD.Visible = false;

                    return;
                }

                //var cameraMatrix = MyAPIGateway.Session.Camera.WorldMatrix;
                //var viewPos = matrix.Translation + matrix.Backward * (HUDSCALE_DIST_ADJUST * (1.0 - settings.hudScale));
                //var markerPos = Vector3D.Lerp(cameraMatrix.Translation, viewPos, settings.markersEyeTracking);

                var camMatrix = MyAPIGateway.Session.Camera.WorldMatrix;
                var camPos = camMatrix.Translation;
                const double MARKER_DISTANCE_CAMERA = 0.01;
                const float ICON_ALPHA = 1f;
                const double AIM_ACCURACY = 0.999;
                var markerMaterial = settings.defaultElements[id].material;

                selectedSort.Clear();

                // GPS markers
                if(settings.markerShowGPS)
                {
                    foreach(var marker in hudGPSMarkers)
                    {
                        if(!marker.ShowOnHud)
                            continue;

                        var dir = marker.Coords - camPos;
                        var dist = (float)dir.Normalize();
                        var pos = camPos + dir * MARKER_DISTANCE_CAMERA;

                        Extensions.TempAddBillboardOriented(markerMaterial, settings.markerColorGPS * ICON_ALPHA, pos, matrix, MARKER_SIZE * settings.markerScale);

                        var dot = dir.Dot(camMatrix.Forward);

                        if(dot >= AIM_ACCURACY)
                        {
                            Extensions.TempAddBillboardOriented(markerMaterial, Color.Gold, pos, matrix, MARKER_SIZE * settings.markerScale * 1.2f);

                            tmp.Clear();
                            tmp.Append(DISPLAY_PAD);
                            MyValueFormatter.AppendDistanceInBestUnit(dist, tmp);
                            tmp.Append(" ").Append(marker.Name);

                            selectedSort.Add(dist, tmp.ToString());
                        }
                    }
                }

                // entity-based markers
                if(settings.markerShowAntennas || settings.markerShowBeacons || settings.markerShowBlocks)
                {
                    foreach(var kv in hudEntityMarkers)
                    {
                        var ent = kv.Key;

                        if(!settings.markerShowAntennas && ent is IMyRadioAntenna)
                            continue;

                        if(!settings.markerShowBeacons && ent is IMyBeacon)
                            continue;

                        if(!settings.markerShowBlocks && !(ent is IMyRadioAntenna || ent is IMyBeacon))
                            continue;

                        var dir = ent.WorldMatrix.Translation - camPos;
                        var dist = (float)dir.Normalize();
                        var pos = camPos + dir * MARKER_DISTANCE_CAMERA;

                        Extensions.TempAddBillboardOriented(markerMaterial, kv.Value.color * ICON_ALPHA, pos, matrix, kv.Value.size * settings.markerScale);

                        // TODO speed arrow?
                        //var block = ent as IMyCubeBlock;
                        //
                        //if(block != null)
                        //{
                        //    var grid = block.CubeGrid;
                        //
                        //    if(grid.Physics != null)
                        //    {
                        //        //var speedDir = grid.Physics.LinearVelocity;
                        //        var speedDir = grid.Physics.GetVelocityAtPoint(ent.WorldMatrix.Translation);
                        //        var speedLen = speedDir.Normalize();
                        //
                        //        if(speedLen > 0.1f)
                        //        {
                        //            //MyTransparentGeometry.AddBillboardOriented("Arrow", Color.Gold * ICON_ALPHA, pos, matrix.Left, matrix.Up, MathHelper.Clamp(speedLen / 100, 0.001f, 0.01f), 0.0005f);
                        //
                        //            const byte CONE_ALPHA = 10;
                        //            const double CONE_OFFSET = 0.00085;
                        //            const float CONE_HEIGHT = 0.0003f;
                        //            const float CONE_RADIUS = 0.00025f;
                        //            const float CONE_LINE_HEIGHT = 0.0006f;
                        //            const float CONE_LINE_RADIUS = 0.0001f;
                        //            const double CONE_LINE_ANGLEDOT = -0.75;
                        //
                        //            var matrixVelocity = matrix;
                        //            AlignToVector(ref matrixVelocity, speedDir);
                        //
                        //            var zScale = MathHelper.Clamp(speedLen / 50, 0.25, 2);
                        //            var xyScale = MathHelper.Clamp(zScale, 0.5, 1.0);
                        //            var vectorScale = new Vector3D(xyScale, xyScale, zScale);
                        //            MatrixD.Rescale(ref matrixVelocity, ref vectorScale);
                        //
                        //            matrixVelocity.Translation = pos + matrixVelocity.Backward * CONE_OFFSET;
                        //
                        //            var color = HUD_ORANGE;
                        //            color.A = CONE_ALPHA;
                        //            var dotView = MyAPIGateway.Session.Camera.WorldMatrix.Forward.Dot(speedDir);
                        //
                        //            // TODO separate arrow quality
                        //            int arrowQuality = MathHelper.Clamp((int)Dev.GetValueScroll("arrowQuality", 8, 1, MyKeys.D5), 1, 100); // arrowQualityIndex[(int)settings.hudQuality];
                        //
                        //            MySimpleObjectDraw.DrawTransparentCone(ref matrixVelocity, CONE_RADIUS, CONE_HEIGHT, ref color, arrowQuality, HUD_TEXTURE);
                        //
                        //            if(dotView > CONE_LINE_ANGLEDOT)
                        //            {
                        //                var lineColor = color * (1 - ((float)Math.Min(dotView, 0) / (float)CONE_LINE_ANGLEDOT));
                        //                matrixVelocity = MatrixD.CreateWorld(pos + matrixVelocity.Backward * (CONE_LINE_HEIGHT / 2), matrixVelocity.Right, matrixVelocity.Backward);
                        //                MySimpleObjectDraw.DrawTransparentCapsule(ref matrixVelocity, CONE_LINE_RADIUS * Math.Min((float)zScale, 1), CONE_LINE_HEIGHT * (float)zScale, ref lineColor, arrowQuality, HUD_TEXTURE);
                        //            }
                        //        }
                        //    }
                        //}

                        var dot = dir.Dot(camMatrix.Forward);

                        if(dot >= AIM_ACCURACY)
                        {
                            Extensions.TempAddBillboardOriented(markerMaterial, Color.Gold, pos, matrix, kv.Value.size * settings.markerScale * 1.2f);

                            tmp.Clear();
                            tmp.Append(DISPLAY_PAD);
                            MyValueFormatter.AppendDistanceInBestUnit(dist, tmp);
                            tmp.Append(" ").Append(kv.Value.name);

                            selectedSort.Add(dist, tmp.ToString());
                        }
                    }
                }

                // Ore markers
                // FIXME whitelist
                /*
                {
                    var oresDist = new double[MyDefinitionManager.Static.VoxelMaterialCount];
                    var oresPos = new Vector3D[oresDist.Length];
                    
                    for(int i = 0; i < oresDist.Length; i++)
                    {
                        oresDist[i] = double.MaxValue;
                        oresPos[i] = Vector3D.Zero;
                    }
                    
                    foreach(var ore in MyHud.OreMarkers)
                    {
                        if(ore.VoxelMap == null || ore.VoxelMap.Closed)
                            continue;
                        
                        foreach(var data in ore.Materials)
                        {
                            var pos = (ore.VoxelMap.WorldMatrix.Translation - (Vector3D)ore.VoxelMap.StorageMin) + data.AverageLocalPosition;
                            var distSq = Vector3D.DistanceSquared(camPos, pos);
                            
                            if(distSq < oresDist[data.Material.Index])
                            {
                                oresDist[data.Material.Index] = distSq;
                                oresPos[data.Material.Index] = pos;
                            }
                        }
                    }
                    
                    for(int i = 0; i < oresPos.Length; i++)
                    {
                        var pos = oresPos[i];
                        
                        if(Math.Abs(pos.X) < 0.0001f && Math.Abs(pos.Y) < 0.0001f && Math.Abs(pos.Z) < 0.0001f)
                            continue;
                        
                        var def = MyDefinitionManager.Static.GetVoxelMaterialDefinition((byte)i);
                        var dir = pos - camPos;
                        dir.Normalize();
                        var target = camPos + dir * MARKER_DISTANCE_CAMERA;
                        
                        MyTransparentGeometry.AddBillboardOriented("HelmetMarker", Color.Purple * ICON_ALPHA, target, matrix.Left, matrix.Up, MARKER_SIZE, 0, true, -1, 0);
                        
                        MyAPIGateway.Utilities.ShowNotification(def.MinedOre, 16);
                    }
                }
                 */

                // handle the marker floating text
                if(selectedSort.Count > 0)
                {
                    if(ghostLCD == null)
                    {
                        PrefabBuilder.CubeBlocks.Clear(); // need no leftovers from previous spawns

                        PrefabTextPanel.SubtypeName = "HelmetHUD_ghostLCD";
                        PrefabTextPanel.FontColor = settings.markerPopupFontColor;
                        PrefabTextPanel.BackgroundColor = settings.markerPopupBGColor;
                        PrefabTextPanel.FontSize = 2;

                        PrefabBuilder.CubeBlocks.Add(PrefabTextPanel);
                        PrefabBuilder.CubeBlocks.Add(PrefabBattery);

                        MyAPIGateway.Entities.RemapObjectBuilder(PrefabBuilder);
                        var ent = MyAPIGateway.Entities.CreateFromObjectBuilder(PrefabBuilder) as MyEntity;
                        ent.IsPreview = true; // don't sync on MP
                        ent.SyncFlag = false; // don't sync on MP
                        ent.Save = false; // don't save this entity
                        MyAPIGateway.Entities.AddEntity(ent, true);
                        ghostLCD = ent;

                        var lcdSlim = (ghostLCD as IMyCubeGrid).GetCubeBlock(Vector3I.Zero);
                        var lcd = lcdSlim.FatBlock as IMyTextPanel;

                        if(lcd == null)
                        {
                            Log.Error("Ghost LCD block not found in grid!");
                            return;
                        }

                        lcd.Render.Transparency = 0.5f;
                        lcd.Render.RemoveRenderObjects();
                        lcd.Render.AddRenderObjects();
                        lcd.SetEmissiveParts("ScreenArea", Color.White, 1f);
                        lcd.SetEmissiveParts("Edges", settings.markerPopupEdgeColor, 0.5f);
                    }

                    const double offset_forward = 0.1;
                    double offset_right = settings.markerPopupOffset.X;
                    double offset_up = settings.markerPopupOffset.Y;

                    var cameraMatrix = MyAPIGateway.Session.Camera.WorldMatrix;
                    cameraMatrix.Translation += cameraMatrix.Forward * offset_forward + cameraMatrix.Right * offset_right + cameraMatrix.Up * offset_up;
                    matrix.Translation += matrix.Forward * offset_forward + matrix.Right * offset_right + matrix.Up * offset_up;
                    matrix.Translation += matrix.Backward * (HUDSCALE_DIST_ADJUST * (1.0 - settings.hudScale)); // remove the HUD scale from the position
                    matrix.Translation = Vector3D.Lerp(cameraMatrix.Translation, matrix.Translation, settings.crosshairSwayRatio);

                    MatrixD.Rescale(ref matrix, settings.markerPopupScale);

                    ghostLCD.SetWorldMatrix(matrix);
                    ghostLCD.Visible = true;
                }
                else if(ghostLCD != null)
                {
                    ghostLCD.Visible = false;
                }

                return;
            }

            // update the vector indicator
            if(id == Icons.VECTOR)
            {
                var cockpit = MyAPIGateway.Session.ControlledObject as IMyCockpit;
                var velDir = (cockpit != null && cockpit.CubeGrid.Physics != null ? cockpit.CubeGrid.Physics.LinearVelocity : characterEntity.Physics.LinearVelocity);
                var velLength = velDir.Normalize();

                matrix.Translation += matrix.Forward * 0.05;
                headPos += matrix.Forward * 0.22;

                var dirV = Vector3D.Normalize(headPos - (matrix.Translation + matrix.Up * elementSettings.posUp));
                var dirH = Vector3D.Normalize(headPos - (matrix.Translation + matrix.Left * elementSettings.posLeft));

                matrix.Translation += (matrix.Left * elementSettings.posLeft) + (matrix.Up * elementSettings.posUp);

                var sphereColor = HUD_DARKGRAY;
                sphereColor.A = 2;

                int sphereQuality = sphereQualityIndex[(int)settings.hudQuality];

                if(sphereQuality > 0)
                    MySimpleObjectDraw.DrawTransparentSphere(ref matrix, 0.004f, ref sphereColor, MySimpleObjectRasterizer.Solid, sphereQuality, MATERIAL_HUD, null);

                bool inGravity = gravityForce >= 0.01f;
                bool isMoving = velLength >= 0.01f;

                if(inGravity || isMoving)
                {
                    float angV = (float)Math.Acos(Math.Round(Vector3D.Dot(matrix.Backward, dirV), 4));
                    float angH = (float)Math.Acos(Math.Round(Vector3D.Dot(matrix.Backward, dirH), 4));

                    if(elementSettings.posUp > 0)
                        angV = -angV;

                    if(elementSettings.posLeft < 0)
                        angH = -angH;

                    var offsetV = MatrixD.CreateFromAxisAngle(matrix.Left, angV);
                    var offsetH = MatrixD.CreateFromAxisAngle(matrix.Up, angH);

                    const byte CONE_ALPHA = 10;
                    const double CONE_OFFSET = 0.0085;
                    const float CONE_HEIGHT = 0.003f;
                    const float CONE_RADIUS = 0.0025f;
                    const float CONE_LINE_HEIGHT = 0.006f;
                    const float CONE_LINE_RADIUS = 0.001f;
                    const double CONE_LINE_ANGLEDOT = -0.75;

                    int arrowQuality = arrowQualityIndex[(int)settings.hudQuality];

                    if(inGravity)
                    {
                        var matrixGravity = matrix;

                        AlignToVector(ref matrixGravity, gravityDir);

                        matrixGravity *= offsetV * offsetH;

                        var zScale = MathHelper.Clamp(gravityForce, 0.25, 2);
                        var xyScale = MathHelper.Clamp(zScale, 0.5, 1.0);
                        var vectorScale = new Vector3D(xyScale, xyScale, zScale);
                        MatrixD.Rescale(ref matrixGravity, ref vectorScale);

                        matrixGravity.Translation = matrix.Translation + matrixGravity.Backward * CONE_OFFSET;

                        var color = HUD_BLUE;
                        color.A = CONE_ALPHA;
                        var dot = MyAPIGateway.Session.Camera.WorldMatrix.Forward.Dot(gravityDir);

                        MySimpleObjectDraw.DrawTransparentCone(ref matrixGravity, CONE_RADIUS, CONE_HEIGHT, ref color, arrowQuality, MATERIAL_HUD);

                        if(dot > CONE_LINE_ANGLEDOT)
                        {
                            var lineColor = color * (1 - ((float)Math.Min(dot, 0) / (float)CONE_LINE_ANGLEDOT));
                            matrixGravity = MatrixD.CreateWorld(matrix.Translation + matrixGravity.Backward * (CONE_LINE_HEIGHT / 2), matrixGravity.Right, matrixGravity.Backward);
                            MySimpleObjectDraw.DrawTransparentCapsule(ref matrixGravity, CONE_LINE_RADIUS * Math.Min((float)zScale, 1), CONE_LINE_HEIGHT * (float)zScale, ref lineColor, arrowQuality, MATERIAL_HUD);
                        }
                    }

                    if(isMoving)
                    {
                        var matrixVelocity = matrix;

                        AlignToVector(ref matrixVelocity, velDir);

                        matrixVelocity *= offsetV * offsetH;

                        var zScale = MathHelper.Clamp(velLength / 50, 0.25, 2);
                        var xyScale = MathHelper.Clamp(zScale, 0.5, 1.0);
                        var vectorScale = new Vector3D(xyScale, xyScale, zScale);
                        MatrixD.Rescale(ref matrixVelocity, ref vectorScale);

                        matrixVelocity.Translation = matrix.Translation + matrixVelocity.Backward * CONE_OFFSET;

                        var color = HUD_ORANGE;
                        color.A = CONE_ALPHA;
                        var dot = MyAPIGateway.Session.Camera.WorldMatrix.Forward.Dot(velDir);

                        MySimpleObjectDraw.DrawTransparentCone(ref matrixVelocity, CONE_RADIUS, CONE_HEIGHT, ref color, arrowQuality, MATERIAL_HUD);

                        if(dot > CONE_LINE_ANGLEDOT)
                        {
                            var lineColor = color * (1 - ((float)Math.Min(dot, 0) / (float)CONE_LINE_ANGLEDOT));
                            matrixVelocity = MatrixD.CreateWorld(matrix.Translation + matrixVelocity.Backward * (CONE_LINE_HEIGHT / 2), matrixVelocity.Right, matrixVelocity.Backward);
                            MySimpleObjectDraw.DrawTransparentCapsule(ref matrixVelocity, CONE_LINE_RADIUS * Math.Min((float)zScale, 1), CONE_LINE_HEIGHT * (float)zScale, ref lineColor, arrowQuality, MATERIAL_HUD);
                        }
                    }
                }

                return;
            }

            // append the oxygen level number to the name and remove the previous entity if changed
            if(id == Icons.OXYGEN_ENV)
            {
                int oxygenEnv = MathHelper.Clamp((int)elementData.value, 0, 2);

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
                    iconEntities[id] = SpawnPrefab(CUBE_HUD_PREFIX + elementSettings.name + (settings.displayQuality == 0 ? "Low" : ""), true);
                }
                else
                {
                    string name = elementSettings.name;

                    // append the oxygen level number to the name and remove the previous entity if changed
                    if(id == Icons.OXYGEN_ENV)
                    {
                        int oxygenEnv = MathHelper.Clamp((int)elementData.value, 0, 2);
                        name += oxygenEnv.ToString();
                    }

                    iconEntities[id] = SpawnPrefab(CUBE_HUD_PREFIX + name);
                }

                if(iconEntities[id] == null)
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
                matrix.Translation += (matrix.Left * elementSettings.posLeft) + (matrix.Up * elementSettings.posUp);

                // align the element to the view and give it the glass curve
                TransformHUD(ref matrix, matrix.Translation + matrix.Forward * 0.05, headPos, -matrix.Up, matrix.Forward);
            }

            iconEntities[id].SetWorldMatrix(matrix);

            if(!elementSettings.hasBar)
                return; // if the HUD element has no bar, stop here.

            if(iconBarEntities[id] == null)
            {
                iconBarEntities[id] = SpawnPrefab(CUBE_HUD_PREFIX + elementSettings.name + "Bar");

                if(iconBarEntities[id] == null)
                    return;
            }

            if(id == Icons.HORIZON)
            {
                var controller = MyAPIGateway.Session.ControlledObject as MyShipController;

                var dotV = Vector3.Dot(naturalDir, controller.WorldMatrix.Forward);
                var dotH = Vector3.Dot(naturalDir, controller.WorldMatrix.Right);

                matrix.Translation += matrix.Up * (dotV * 0.035);

                var t = matrix.Translation;
                matrix *= MatrixD.CreateFromAxisAngle(matrix.Backward, MathHelper.ToRadians(dotH * 90));
                matrix.Translation = t;

                // horizon bar is not a resizable or blinking bar so just update it here and stop
                iconBarEntities[id].SetWorldMatrix(matrix);
                return;
            }

            // blink this element in sync with the warning icon if its under the warning percent
            if(warningBlinkOn && elementData.value <= elementSettings.warnPercent && hudElementData[Icons.WARNING].show)
            {
                var pos = matrix.Translation + (settings.elements[id].flipHorizontal ? matrix.Right : matrix.Left) * 0.00325;
                MyTransparentGeometry.AddBillboardOriented(MATERIAL_BAR_WARNING_BG, new Color(255, 0, 0) * 0.25f, pos, matrix.Left, matrix.Up, 0.016f, 0.004f);

                //iconBarEntities[id].Visible = false;
                //return;
            }

            // calculate the bar size
            double scale = Math.Min(Math.Max((elementData.value * HUD_BAR_MAX_SCALE) / 100, 0), HUD_BAR_MAX_SCALE);
            var align = (elementSettings.flipHorizontal ? matrix.Left : matrix.Right);
            matrix.Translation += (align * (scale / 100.0)) - (align * (0.00955 + (0.0008 * (0.5 - (elementData.value / 100)))));
            matrix.M11 *= scale;
            matrix.M12 *= scale;
            matrix.M13 *= scale;

            iconBarEntities[id].Visible = true;
            iconBarEntities[id].SetWorldMatrix(matrix);
        }

        private void AlignToVector(ref MatrixD matrix, Vector3D direction)
        {
            var vector3D = new Vector3D(0.0, 0.0, 1.0);
            Vector3D up;
            double z = direction.Z;

            if(z > -0.99999 && z < 0.99999)
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
                if(!name.StartsWith(""))
                    return null;

                PrefabBuilder.CubeBlocks.Clear(); // need no leftovers from previous spawns

                if(isDisplay)
                {
                    PrefabTextPanel.SubtypeName = name;
                    PrefabTextPanel.FontColor = settings.displayFontColor;
                    PrefabTextPanel.BackgroundColor = settings.displayBgColor;
                    PrefabTextPanel.FontSize = DISPLAY_FONT_SIZE;

                    if(settings.displayBorderColor.HasValue)
                        PrefabTextPanel.ColorMaskHSV = settings.displayBorderColor.Value.ToVector3();
                    else if(characterEntity != null)
                        PrefabTextPanel.ColorMaskHSV = characterEntity.Render.ColorMaskHsv;
                    else
                        PrefabTextPanel.ColorMaskHSV = new Vector3(0, -1, 0); // default gray

                    //PrefabTextPanel.PublicDescription = GetDisplayData(true);
                    // ^ this makes the LCD not emissive for some reason, so using this instead:
                    skipDisplay = 999;
                    lastDisplayText = null;

                    PrefabBuilder.CubeBlocks.Add(PrefabTextPanel);
                    PrefabBuilder.CubeBlocks.Add(PrefabBattery);

                    var def = MyDefinitionManager.Static.GetCubeBlockDefinition(new MyDefinitionId(typeof(MyObjectBuilder_TextPanel), name)) as MyTextPanelDefinition;

                    if(def != null)
                        def.TextureResolution = settings.displayResolution;
                }
                else
                {
                    PrefabCubeBlock.SubtypeName = name;
                    PrefabBuilder.CubeBlocks.Add(PrefabCubeBlock);
                }

                MyAPIGateway.Entities.RemapObjectBuilder(PrefabBuilder);
                var ent = (MyEntity)MyAPIGateway.Entities.CreateFromObjectBuilder(PrefabBuilder);
                ent.IsPreview = true; // don't sync on MP
                ent.SyncFlag = false; // don't sync on MP
                ent.Save = false; // don't save this entity
                MyAPIGateway.Entities.AddEntity(ent, true);
                return ent;
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            return null;
        }

        private static SerializableVector3 PrefabVector0 = new SerializableVector3(0, 0, 0);
        private static SerializableVector3I PrefabVectorI0 = new SerializableVector3I(0, 0, 0);
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
            Name = "HelmetMod",
            DisplayName = "HelmetMod",
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
        private static MyObjectBuilder_TextPanel PrefabTextPanel = new MyObjectBuilder_TextPanel()
        {
            EntityId = 1,
            Min = PrefabVectorI0,
            BlockOrientation = PrefabOrientation,
            ShareMode = MyOwnershipShareModeEnum.None,
            DeformationRatio = 0,
            ShowOnHUD = false,
            //ShowText = ShowTextOnScreenFlag.PUBLIC, // HACK not whitelisted anymore...
            FontSize = DISPLAY_FONT_SIZE,
        };
        private static MyObjectBuilder_BatteryBlock PrefabBattery = new MyObjectBuilder_BatteryBlock()
        {
            SubtypeName = CUBE_HUD_PREFIX + "battery",
            Min = new SerializableVector3I(0, 0, -10),
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
            altitude = float.MaxValue;

            foreach(var planet in planets)
            {
                if(planet.Closed || planet.MarkedForClose)
                    continue;

                var dir = planet.PositionComp.GetPosition() - point;
                var gravComp = (MySphericalNaturalGravityComponent)planet.Components.Get<MyGravityProviderComponent>();

                if(dir.LengthSquared() <= gravComp.GravityLimitSq)
                {
                    altitude = Math.Min((float)Vector3D.Distance(point, planet.GetClosestSurfacePointGlobal(ref point)), altitude);
                    dir.Normalize();
                    naturalDir += dir * gravComp.GetGravityMultiplier(point);
                }
            }

            if(altitude >= float.MaxValue - 1)
                altitude = 0;

            naturalForce = naturalDir.Length();

            foreach(var generator in gravityGenerators.Values)
            {
                if(!generator.IsWorking)
                    continue;

                var flat = generator as IMyGravityGenerator;

                if(flat != null)
                {
                    var box = new MyOrientedBoundingBoxD(flat.WorldMatrix.Translation, flat.FieldSize / 2, Quaternion.CreateFromRotationMatrix(flat.WorldMatrix));

                    if(box.Contains(ref point))
                        artificialDir += flat.WorldMatrix.Down * (flat.GravityAcceleration / G_ACCELERATION);

                    continue;
                }

                var sphere = generator as IMyGravityGeneratorSphere;

                if(sphere != null)
                {
                    var dir = sphere.WorldMatrix.Translation - point;

                    if(dir.LengthSquared() <= (sphere.Radius * sphere.Radius))
                    {
                        dir.Normalize();
                        artificialDir += dir * (sphere.GravityAcceleration / G_ACCELERATION);
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

        public void MessageEntered(string msg, ref bool visible)
        {
            if(!msg.StartsWith("/helmet", StringComparison.OrdinalIgnoreCase))
                return;

            visible = false;
            msg = msg.Substring("/helmet".Length).Trim().ToLower();

            if(msg.Length > 0)
            {
                if(msg.StartsWith("for fov", StringComparison.Ordinal))
                {
                    msg = msg.Substring("for fov".Length).Trim();

                    float fov = 0;

                    if(msg.Length == 0)
                    {
                        fov = MathHelper.ToDegrees(MyAPIGateway.Session.Config.FieldOfView);
                        MyVisualScriptLogicProvider.SendChatMessage("Your FOV is: " + fov, MOD_NAME, 0, MyFontEnum.Blue);
                    }
                    else if(!float.TryParse(msg, out fov))
                    {
                        MyVisualScriptLogicProvider.SendChatMessage("Invalid float number: " + msg, MOD_NAME, 0, MyFontEnum.Red);
                    }

                    if(fov > 0)
                    {
                        settings.ScaleForFOV(fov);
                        settings.Save();
                        MyVisualScriptLogicProvider.SendChatMessage("HUD and helmet scale set to " + settings.scale + "; saved to config.", MOD_NAME, 0, MyFontEnum.Green);
                    }

                    return;
                }
                else if(msg.StartsWith("scale", StringComparison.Ordinal))
                {
                    msg = msg.Substring("scale".Length).Trim();

                    if(msg.Length == 0)
                    {
                        MyVisualScriptLogicProvider.SendChatMessage("Visor scale = " + settings.visorScale, MOD_NAME, 0, MyFontEnum.Blue);
                        return;
                    }

                    double d;

                    if(double.TryParse(msg, out d))
                    {
                        settings.visorScale = MathHelper.Clamp(d, Settings.MIN_VISOR_SCALE, Settings.MAX_VISOR_SCALE);
                        settings.Save();
                        MyVisualScriptLogicProvider.SendChatMessage("Visor scale set to " + settings.visorScale + "; saved to config.", MOD_NAME, 0, MyFontEnum.Green);
                    }
                    else
                    {
                        MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Invalid float number: " + msg);
                    }

                    return;
                }
                else if(msg.StartsWith("hud scale", StringComparison.Ordinal))
                {
                    msg = msg.Substring("hud scale".Length).Trim();

                    if(msg.Length == 0)
                    {
                        MyVisualScriptLogicProvider.SendChatMessage("HUD scale = " + settings.hudScale, MOD_NAME, 0, MyFontEnum.Blue);
                        return;
                    }

                    double scale;

                    if(double.TryParse(msg, out scale))
                    {
                        scale = Math.Min(Math.Max(scale, Settings.MIN_HUDSCALE), Settings.MAX_HUDSCALE);
                        settings.hudScale = scale;
                        settings.Save();
                        MyVisualScriptLogicProvider.SendChatMessage("HUD scale set to " + scale + "; saved to config.", MOD_NAME, 0, MyFontEnum.Green);
                    }
                    else
                    {
                        MyVisualScriptLogicProvider.SendChatMessage("Invalid float number: " + msg, MOD_NAME, 0, MyFontEnum.Red);
                    }

                    return;
                }
                else if(msg.StartsWith("off", StringComparison.Ordinal))
                {
                    MyVisualScriptLogicProvider.SendChatMessage("Turned OFF; saved to config.", MOD_NAME, 0, MyFontEnum.Green);
                    settings.enabled = false;
                    settings.Save();
                    return;
                }
                else if(msg.StartsWith("on", StringComparison.Ordinal))
                {
                    MyVisualScriptLogicProvider.SendChatMessage("Turned ON; saved to config.", MOD_NAME, 0, MyFontEnum.Green);
                    settings.enabled = true;
                    settings.Save();
                    return;
                }
                else if(msg.StartsWith("hud off", StringComparison.Ordinal))
                {
                    MyVisualScriptLogicProvider.SendChatMessage("HUD turned OFF; saved to config.", MOD_NAME, 0, MyFontEnum.Green);
                    settings.hud = false;
                    settings.Save();
                    return;
                }
                else if(msg.StartsWith("hud on", StringComparison.Ordinal))
                {
                    MyVisualScriptLogicProvider.SendChatMessage("HUD turned ON; saved to config.", MOD_NAME, 0, MyFontEnum.Green);
                    settings.hud = true;
                    settings.Save();
                    return;
                }
                else if(msg.StartsWith("reload", StringComparison.Ordinal))
                {
                    if(settings.Load())
                        MyVisualScriptLogicProvider.SendChatMessage("Reloaded and re-saved config.", MOD_NAME, 0, MyFontEnum.Green);
                    else
                        MyVisualScriptLogicProvider.SendChatMessage("Config created with the current settings.", MOD_NAME, 0, MyFontEnum.Green);

                    settings.Save();

                    RemoveHelmet(removeHud: true);

                    // TODO feature: lights
                    //UpdateLight(0, remove: true);
                    //UpdateLight(1, remove: true);
                    //SetCharacterLightOffsets(settings.lightReplace > 0);

                    return;
                }
                else if(msg.StartsWith("glass off", StringComparison.Ordinal))
                {
                    MyVisualScriptLogicProvider.SendChatMessage("Glass reflections turned OFF; saved to config.", MOD_NAME, 0, MyFontEnum.Green);
                    settings.glassReflections = false;
                    settings.Save();
                    RemoveHelmet();
                    return;
                }
                else if(msg.StartsWith("glass on", StringComparison.Ordinal))
                {
                    MyVisualScriptLogicProvider.SendChatMessage("Glass reflections turned ON; saved to config.", MOD_NAME, 0, MyFontEnum.Green);
                    settings.glassReflections = true;
                    settings.Save();
                    RemoveHelmet();
                    return;
                }
                else if(msg.StartsWith("lcd off", StringComparison.Ordinal))
                {
                    MyVisualScriptLogicProvider.SendChatMessage("LCD turned OFF; saved to config.", MOD_NAME, 0, MyFontEnum.Green);
                    settings.elements[Icons.DISPLAY].show = 0;
                    settings.Save();
                    RemoveHelmet();
                    return;
                }
                else if(msg.StartsWith("lcd on", StringComparison.Ordinal))
                {
                    MyVisualScriptLogicProvider.SendChatMessage("LCD turned ON; saved to config.", MOD_NAME, 0, MyFontEnum.Green);
                    settings.elements[Icons.DISPLAY].show = 3;
                    settings.Save();
                    RemoveHelmet();
                    return;
                }
            }

            MyAPIGateway.Utilities.ShowMissionScreen("Helmet Mod Commands", "", "You can type these commands in the chat.", HELP_COMMANDS, null, "Close");
        }

        private byte skipUpdateData = 200;

        private bool gotBlocksThisTick = false;

        private float shipMass = 0;
        private int shipLGs = 0;
        private int shipLGsReady = 0;
        private int shipLGsLocked = 0;
        private float shipPowerOutput = 0;
        private float shipPowerMaxOutput = 0;

        private float charInvMass = 0;
        private float charInvVolume = 0;
        private int charInvFillPercent = 0;

        private static readonly string[] massUnitNames = new[]
        {
            "g",
            "kg",
            "T",
            "MT"
        };

        private static readonly float[] massUnitMultipliers = new[]
        {
            0.001f,
            1f,
            1000f,
            1000000f
        };

        private static readonly int[] massUnitDigits = new[]
        {
            0,
            0,
            2,
            2
        };

        public string GetDisplayData(bool forceUpdate = false)
        {
            gotBlocksThisTick = false;

            if(characterEntity == null)
                return null;

            if(!forceUpdate && ++skipDisplay % (60 / settings.displayUpdateRate) != 0)
                return null;

            skipDisplay = 0;
            str.Clear();

            if(characterEntity.Integrity <= 0)
            {
                str.Append(DISPLAY_PAD).Append("ERROR #404:").AppendLine();
                str.Append(DISPLAY_PAD).Append("USER LIFESIGNS NOT FOUND.").AppendLine();
            }
            else try
                {
                    var cockpit = MyAPIGateway.Session.ControlledObject as IMyShipController;

                    if(++skipUpdateData > 5)
                    {
                        skipUpdateData = 0;

                        var charInv = ((MyEntity)characterEntity).GetInventory(0);

                        if(charInv != null)
                        {
                            charInvVolume = (float)charInv.CurrentVolume;
                            charInvMass = (float)charInv.CurrentMass;
                            charInvFillPercent = (int)((charInvVolume / (float)charInv.MaxVolume) * 100);
                        }
                        else
                        {
                            charInvVolume = 0;
                            charInvFillPercent = 0;
                            charInvMass = 0;
                        }

                        if(cockpit != null) // in ship, update ship stuff
                        {
                            var grid = cockpit.CubeGrid as MyCubeGrid;

                            float baseMass;
                            grid.GetCurrentMass(out baseMass, out shipMass);

                            shipLGs = 0;
                            shipLGsReady = 0;
                            shipLGsLocked = 0;
                            shipPowerOutput = 0;
                            shipPowerMaxOutput = 0;

                            var fatBlocks = grid.GetFatBlocks();

                            foreach(var b in fatBlocks)
                            {
                                var lg = b as IMyLandingGear;

                                if(lg != null)
                                {
                                    shipLGs++;

                                    // HACK SpaceEngineers.Game.ModAPI.Ingame.LandingGearMode is prohibited...
                                    if((int)lg.LockMode == 1) // ready to lock?
                                        shipLGsReady++;
                                    else if(lg.IsLocked)
                                        shipLGsLocked++;
                                }

                                MyResourceSourceComponent source;

                                if(b.Components.TryGet<MyResourceSourceComponent>(out source))
                                {
                                    shipPowerOutput += source.CurrentOutput;
                                    shipPowerMaxOutput += source.MaxOutput;
                                }
                            }
                        }
                    }

                    float speed = 0;
                    float accel = 0;
                    char accelSymbol = '-';

                    if(cockpit != null)
                    {
                        try
                        {
                            var block = MyAPIGateway.Session.ControlledObject.Entity as IMyCubeBlock;

                            if(block != null && block.CubeGrid != null && block.CubeGrid.Physics != null)
                            {
                                speed = (float)Math.Round(block.CubeGrid.Physics.LinearVelocity.Length(), 2);
                                accel = speed > 0 ? (float)Math.Round(block.CubeGrid.Physics.LinearAcceleration.Length(), 2) : 0;
                            }
                        }
                        catch(Exception e)
                        {
                            MyAPIGateway.Utilities.ShowNotification("ERROR IN TRY-CATCH #1", 500000, MyFontEnum.Red);
                            Log.Error(e);
                        }
                    }
                    else
                    {
                        var player = MyAPIGateway.Session.ControlledObject.Entity;

                        if(player != null && player.Physics != null)
                        {
                            speed = (float)Math.Round(player.Physics.LinearVelocity.Length(), 2);
                            accel = speed > 0 ? (float)Math.Round(player.Physics.LinearAcceleration.Length(), 2) : 0;
                        }
                    }

                    if(speed >= prevSpeed)
                        accelSymbol = '+';

                    prevSpeed = speed;
                    string unit = "m/s";
                    string accelUnit = "m/s²";

                    if(settings.displaySpeedUnit == SpeedUnits.kph)
                    {
                        speed *= 3.6f;
                        unit = "km/h";

                        accel *= 3.6f;
                        accelUnit = "km/h²";
                    }

                    str.Append(DISPLAY_PAD).Append("Speed: ").Append(speed.ToString(FLOAT_FORMAT)).Append(unit).Append(" (").Append(accelSymbol).Append(accel.ToString(FLOAT_FORMAT)).Append(accelUnit).Append(")").AppendLine();

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

                    if(cockpit != null)
                    {
                        str.Append(DISPLAY_PAD).Append("Ship mass: ");
                        MyValueFormatter.AppendFormattedValueInBestUnit(shipMass, massUnitNames, massUnitMultipliers, massUnitDigits, str);
                        str.Append(" + ");
                        MyValueFormatter.AppendFormattedValueInBestUnit(characterDefinition.Mass + charInvMass, massUnitNames, massUnitMultipliers, massUnitDigits, str);
                        str.AppendLine();

                        str.Append(DISPLAY_PAD).Append("Power: ");

                        if(shipPowerMaxOutput > 0)
                        {
                            var powerPercent = Math.Round((shipPowerOutput / shipPowerMaxOutput) * 100, 2);

                            str.Append(powerPercent.ToString(FLOAT_FORMAT)).Append('%');

                            var distributor = (cockpit as MyShipController).GridResourceDistributor;
                            var time = distributor.RemainingFuelTimeByType(MyResourceDistributorComponent.ElectricityId);

                            str.Append(" (");
                            MyValueFormatter.AppendTimeInBestUnit(time * 3600f, str);
                            str.Append(")");
                        }
                        else
                        {
                            str.Append("None");
                        }

                        str.AppendLine();

                        str.Append(DISPLAY_PAD).Append("Landing gears: ");
                        str.Append(shipLGsReady).Append(" R, ");
                        str.Append(shipLGsLocked).Append(" L, ");
                        str.Append(shipLGs).Append(" T");
                        str.AppendLine();
                    }
                    else
                    {
                        str.Append(DISPLAY_PAD).Append("Inventory: ");
                        MyValueFormatter.AppendVolumeInBestUnit(charInvVolume, str);
                        str.Append(" (");

                        if(MyAPIGateway.Session.CreativeMode)
                            str.Append("--");
                        else
                            str.Append(charInvFillPercent);

                        str.Append("%)");
                        str.AppendLine();

                        str.Append(DISPLAY_PAD).Append("Mass: ");

                        MyValueFormatter.AppendFormattedValueInBestUnit(characterDefinition.Mass, massUnitNames, massUnitMultipliers, massUnitDigits, str);
                        str.Append(" + ");
                        MyValueFormatter.AppendFormattedValueInBestUnit(charInvMass, massUnitNames, massUnitMultipliers, massUnitDigits, str);
                        str.AppendLine();

                        str.Append(DISPLAY_PAD);

                        if(MyAPIGateway.Session.CreativeMode)
                        {
                            str.Append("No resource is being drained.");
                        }
                        else
                        {
                            float power = characterEntity.SuitEnergyLevel * 100;
                            float o2 = characterEntity.GetSuitGasFillLevel(DEFID_OXYGEN) * 100;
                            float h = characterEntity.GetSuitGasFillLevel(DEFID_HYDROGEN) * 100;
                            long now = DateTime.UtcNow.Ticks;

                            if(Math.Abs(prevBattery - power) > float.Epsilon)
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

                            if(Math.Abs(prevO2 - o2) > float.Epsilon)
                            {
                                float elapsed = (float)TimeSpan.FromTicks(now - prevO2Time).TotalSeconds;
                                etaO2 = (0 - o2) / ((o2 - prevO2) / elapsed);

                                if(etaO2 < 0)
                                    etaO2 = float.PositiveInfinity;

                                prevO2 = o2;
                                prevO2Time = now;
                            }

                            float elapsedH = (float)TimeSpan.FromTicks(now - prevHTime).TotalSeconds;

                            if(Math.Abs(prevH - h) > float.Epsilon || elapsedH > (TimeSpan.TicksPerSecond * 2))
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

                    if(!cubeBuilder && cockpit == null && holdingTool != null)
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
                        var b = Sandbox.Game.Gui.MyHud.BlockInfo;

                        if(buildTool)
                        {
                            var casterComp = holdingTool.Components.Get<MyCasterComponent>();

                            if(casterComp != null && casterComp.HitBlock == null)
                                b = null;
                        }

                        if(b != null && !string.IsNullOrEmpty(b.BlockName))
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
                                str.Append(b.BlockName).AppendLine();

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
                        AppendOresInRange(str);
                    }
                    else if(handWeapon)
                    {
                        var gun = (IMyHandheldGunObject<MyGunBase>)holdingTool;
                        var gunUser = (IMyGunBaseUser)holdingTool;
                        var physWepDef = MyDefinitionManager.Static.TryGetPhysicalItemDefinition(gunUser.PhysicalItemId) as MyWeaponItemDefinition;
                        MyWeaponDefinition wepDef;

                        if(physWepDef != null && MyDefinitionManager.Static.TryGetWeaponDefinition(physWepDef.WeaponDefinitionId, out wepDef))
                        {
                            str.Append(physWepDef.DisplayNameText).AppendLine();

                            var inv = characterEntity.GetInventory();

                            if(inv != null)
                            {
                                var currentMag = gun.GunBase.CurrentAmmoMagazineDefinition.DisplayNameText;
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
                                            tmp.Append("[x] ");
                                        else
                                            tmp.Append("[ ] ");
                                    }

                                    float mags = (float)inv.GetItemAmount(wepDef.AmmoMagazinesId[i], MyItemFlags.None);
                                    string magName = magDef.DisplayNameText;

                                    if(magName.Length > 22)
                                        magName = magName.Substring(0, 20) + "..";

                                    if(magId.SubtypeName == currentMag)
                                        rounds = gun.CurrentMagazineAmmunition + mags * magDef.Capacity;

                                    tmp.Append(mags).Append("x ").Append(magName).AppendLine();
                                }

                                str.Append("Rounds: ").Append(rounds).AppendLine();
                                str.Append(tmp);
                                tmp.Clear();
                            }
                            else
                            {
                                str.AppendLine("ERROR: no inventory!");
                            }
                        }
                    }
                    else if(cockpit != null)
                    {
                        try
                        {
                            var toolbarObj = (MyObjectBuilder_ShipController)cockpit.GetObjectBuilderCubeBlock(false);
                            var grid = cockpit.CubeGrid as IMyCubeGrid;

                            if(toolbarObj.Toolbar != null && toolbarObj.Toolbar.SelectedSlot.HasValue)
                            {
                                var item = toolbarObj.Toolbar.Slots[toolbarObj.Toolbar.SelectedSlot.Value];
                                var weapon = item.Data as MyObjectBuilder_ToolbarItemWeapon;

                                if(weapon != null)
                                {
                                    bool shipWelder = weapon.DefinitionId.TypeId == typeof(MyObjectBuilder_ShipWelder);

                                    if(shipWelder || weapon.DefinitionId.TypeId == typeof(MyObjectBuilder_ShipGrinder))
                                    {
                                        GetBlocksOnce(grid);

                                        float cargo = 0;
                                        float cargoMax = 0;
                                        float cargoMass = 0;
                                        int tools = 0;

                                        foreach(var block in terminalBlocks)
                                        {
                                            if(shipWelder ? block is IMyShipWelder : block is IMyShipGrinder)
                                            {
                                                tools++;
                                                var inv = block.GetInventory();

                                                if(inv != null)
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
                                                    return null;
                                                }

                                                var block = slimBlock.FatBlock;
                                                var def = slimBlock.BlockDefinition as MyCubeBlockDefinition;
                                                var terminal = block as IMyTerminalBlock;

                                                if(terminal != null)
                                                    str.Append("\"").Append(terminal.CustomName).Append("\"").AppendLine();
                                                else
                                                    str.Append(def.DisplayNameText).AppendLine();

                                                str.Append("Integrity: ").Append(Math.Round((slimBlock.BuildIntegrity / slimBlock.MaxIntegrity) * 100, 2)).Append("% (").Append(Math.Round(def.CriticalIntegrityRatio * 100, 0)).Append("% / ").Append(Math.Round(def.OwnershipIntegrityRatio * 100, 0)).Append("%)").AppendLine();

                                                var missing = new Dictionary<string, int>();
                                                slimBlock.GetMissingComponents(missing);
                                                int missingCount = missing.Count;

                                                if(missingCount > 0)
                                                {
                                                    const int max = 3;
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
                                        GetBlocksOnce(grid);

                                        float gatlingAmmo = 0;
                                        float containerAmmo = 0;
                                        int gatlingGuns = 0;
                                        MyInventory inv;
                                        MyAmmoMagazineDefinition magDef = null;

                                        foreach(var block in terminalBlocks)
                                        {
                                            if(block is IMySmallGatlingGun)
                                            {
                                                var gun = (IMyGunObject<MyGunBase>)block;
                                                gatlingGuns++;
                                                gatlingAmmo = gun.GetAmmunitionAmount();
                                                magDef = gun.GunBase.CurrentAmmoMagazineDefinition;
                                            }
                                        }

                                        if(magDef != null)
                                        {
                                            foreach(var block in terminalBlocks)
                                            {
                                                if(block is IMyCargoContainer && (block as MyEntity).TryGetInventory(out inv))
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
                                        GetBlocksOnce(grid);

                                        bool reloadable = weapon.DefinitionId.TypeId == typeof(MyObjectBuilder_SmallMissileLauncherReload);
                                        int launchers = 0;
                                        float launcherAmmo = 0;
                                        float containerAmmo = 0;
                                        MyAmmoMagazineDefinition magDef = null;

                                        foreach(var block in terminalBlocks)
                                        {
                                            if(block is IMySmallMissileLauncher && reloadable == (block is IMySmallMissileLauncherReload))
                                            {
                                                var gun = (IMyGunObject<MyGunBase>)block;
                                                launchers++;
                                                launcherAmmo = gun.GetAmmunitionAmount();
                                                magDef = gun.GunBase.CurrentAmmoMagazineDefinition;
                                            }
                                        }

                                        foreach(var block in terminalBlocks)
                                        {
                                            if(block is IMyCargoContainer)
                                            {
                                                var inv = block.GetInventory();

                                                if(inv != null)
                                                    containerAmmo += (float)inv.GetItemAmount(magDef.Id, MyItemFlags.None) * magDef.Capacity;
                                            }
                                        }

                                        str.Append(launchers).Append("x missile launchers: ").Append((int)launcherAmmo).AppendLine();
                                        str.Append("Ammo in containers: ").Append((int)containerAmmo).AppendLine();
                                    }
                                    else if(weapon.DefinitionId.TypeId == typeof(MyObjectBuilder_Drill))
                                    {
                                        GetBlocksOnce(grid);

                                        float cargo = 0;
                                        float cargoMax = 0;
                                        float cargoMass = 0;
                                        int containers = 0;
                                        float drillVol = 0;
                                        float drillVolMax = 0;
                                        float drillMass = 0;
                                        int drills = 0;

                                        foreach(var block in terminalBlocks)
                                        {
                                            if(block is IMyShipDrill)
                                            {
                                                drills++;
                                                var inv = block.GetInventory();

                                                if(inv != null)
                                                {
                                                    drillVol += (float)inv.CurrentVolume;
                                                    drillVolMax += (float)inv.MaxVolume;
                                                    drillMass += (float)inv.CurrentMass;
                                                }
                                            }
                                            else if(block is IMyCargoContainer)
                                            {
                                                containers++;
                                                var inv = block.GetInventory();

                                                if(inv != null)
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
                            else if(cockpit.CustomName != null)
                            {
                                var blockName = cockpit.CustomName.ToLower();

                                if(blockName.Contains("ores"))
                                {
                                    AppendOresInRange(str);
                                }

                                // TODO I should probably remove these ? - because selecting weapons shows this data anyway
                                bool showGatling = blockName.Contains("gatling");
                                bool showLaunchers = blockName.Contains("launchers");

                                // TODO add turrets status?
                                bool showAmmo = blockName.Contains("ammo");
                                bool showCargo = blockName.Contains("cargo");
                                bool showOxygen = blockName.Contains("oxygen");
                                bool showHydrogen = blockName.Contains("hydrogen");

                                if(showGatling || showLaunchers || showAmmo || showCargo || showOxygen || showHydrogen)
                                {
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

                                    GetBlocksOnce(grid);

                                    foreach(var block in terminalBlocks)
                                    {
                                        if(showGatling && block is IMySmallGatlingGun)
                                        {
                                            var gun = (IMyGunObject<MyGunBase>)block;
                                            gatlingGuns++;
                                            gatlingAmmo += gun.GetAmmunitionAmount();
                                        }
                                        else if(showLaunchers && block is IMySmallMissileLauncher)
                                        {
                                            var gun = (IMyGunObject<MyGunBase>)block;
                                            launchers++;
                                            launchersAmmo += gun.GetAmmunitionAmount();
                                        }
                                        else if(showCargo && block is IMyCargoContainer)
                                        {
                                            var inv = block.GetInventory();

                                            if(inv != null)
                                            {
                                                cargo += (float)inv.CurrentVolume;
                                                totalCargo += (float)inv.MaxVolume;
                                                cargoMass += (float)inv.CurrentMass;
                                                containers++;
                                            }
                                        }
                                        else if(showAmmo && (block is IMyLargeTurretBase || block is IMySmallGatlingGun || block is IMyCargoContainer))
                                        {
                                            var inv = block.GetInventory();

                                            if(inv != null)
                                            {
                                                mags += (float)inv.GetItemAmount(AMMO_BULLETS.GetId(), MyItemFlags.None);
                                                missiles += (float)inv.GetItemAmount(AMMO_MISSILES.GetId(), MyItemFlags.None);
                                            }
                                        }
                                        else if((showOxygen || showHydrogen) && block is IMyGasTank)
                                        {
                                            var tank = block as IMyGasTank;
                                            var def = (MyGasTankDefinition)tank.SlimBlock.BlockDefinition;

                                            if(showHydrogen && def.StoredGasId == MyResourceDistributorComponent.HydrogenId)
                                            {
                                                hydrogen += (float)tank.FilledRatio;
                                                hydrogenTanks++;
                                            }
                                            else if(showOxygen && def.StoredGasId == MyResourceDistributorComponent.OxygenId)
                                            {
                                                oxygen += (float)tank.FilledRatio;
                                                oxygenTanks++;
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
                        catch(Exception e)
                        {
                            str.AppendLine("ERROR in cockpit code.");
                            Log.Error(e);
                        }
                    }
                }
                catch(Exception e)
                {
                    str.Append("ERROR, SEND LOG TO AUTHOR.").AppendLine();
                    Log.Error(e);
                }

            var text = str.ToString();
            str.Clear();

            if(forceUpdate || lastDisplayText == null || text != lastDisplayText)
            {
                lastDisplayText = text;
                return text;
            }

            return null;
        }

        /// <summary>
        /// Used in conjuction with GetDisplayData() to update terminalBlocks only once per GetDisplayData() call regardless of how many times this method is called
        /// </summary>
        private void GetBlocksOnce(IMyCubeGrid grid)
        {
            if(gotBlocksThisTick)
                return;

            var gridSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);

            terminalBlocks.Clear();
            gridSystem.GetBlocks(terminalBlocks);
        }

        private void AppendOresInRange(StringBuilder str)
        {
            // FIXME whitelist screwed this up
            /*
            str.Append("Ore clusters in range:").AppendLine();
            
            var ores = new Dictionary<byte, int>(); // finish & optimize
            
            foreach(var ore in MyHud.OreMarkers)
            {
                if(ore.VoxelMap == null || ore.VoxelMap.Closed)
                    continue;
                
                foreach(var data in ore.Materials)
                {
                    var index = data.Material.Index;
                    
                    if(ores.ContainsKey(index))
                    {
                        ores[index]++;
                    }
                    else
                    {
                        ores.Add(index, 1);
                    }
                }
            }
            
            foreach(var kv in ores)
            {
                var voxelDef = MyDefinitionManager.Static.GetVoxelMaterialDefinition(kv.Key);
                str.Append(kv.Value).Append("x ").Append(voxelDef.MinedOre).AppendLine();
            }
             */
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_TextPanel), false, "HelmetHUD_display", "HelmetHUD_displayLow")] // LCD blinking workaround, tied with entity update
    public class HelmetLCD : MyGameLogicComponent
    {
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
        }

        public override void UpdateAfterSimulation()
        {
            try
            {
                var panel = (IMyTextPanel)Entity;

                if(Helmet.instance.tick % Helmet.SKIP_TICKS_HUD == 0) // update LCD border color
                {
                    var panelGrid = panel.CubeGrid as MyCubeGrid;
                    var panelSlim = panelGrid.GetCubeBlock(panel.Position) as IMySlimBlock;

                    if(Helmet.instance.settings.displayBorderColor.HasValue)
                    {
                        if(Vector3.DistanceSquared(panelSlim.GetColorMask(), Helmet.instance.settings.displayBorderColor.Value) > 0.01f)
                        {
                            // can't cast to MySlimBlock because of whitelist restrictions so using GetCubeBlock() instead
                            panelGrid.ChangeColor(panelGrid.GetCubeBlock(panelSlim.Position), Helmet.instance.settings.displayBorderColor.Value);
                            panel.SetEmissiveParts("ScreenArea", Color.White, 1); // fixes the screen being non-emissive
                            Helmet.instance.lastDisplayText = null; // force text rewrite
                        }
                    }
                    else
                    {
                        var charColor = Helmet.instance.characterEntity.Render.ColorMaskHsv;

                        if(Vector3.DistanceSquared(panelSlim.GetColorMask(), charColor) > 0.01f)
                        {
                            // can't cast to MySlimBlock because of whitelist restrictions so using GetCubeBlock() instead
                            panelGrid.ChangeColor(panelGrid.GetCubeBlock(panelSlim.Position), charColor);
                            panel.SetEmissiveParts("ScreenArea", Color.White, 1); // fixes the screen being non-emissive
                            Helmet.instance.lastDisplayText = null; // force text rewrite
                        }
                    }
                }

                var text = Helmet.instance.GetDisplayData();

                if(text != null)
                {
                    panel.WritePublicText(text, false);
                    panel.ShowPublicTextOnScreen();
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_TextPanel), false, "HelmetHUD_ghostLCD")] // LCD blinking workaround, tied with entity update
    public class GhostLCD : MyGameLogicComponent
    {
        private string prevText = "";
        private readonly StringBuilder str = new StringBuilder();

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.EACH_10TH_FRAME;
        }

        public override void UpdateAfterSimulation10()
        {
            try
            {
                var selected = Helmet.instance.selectedSort;

                if(selected.Count > 0)
                {
                    str.Clear();
                    var values = selected.Values;
                    int i = 0;

                    foreach(var kv in selected)
                    {
                        if(++i > Helmet.MARKERS_MAX_SELECTED)
                        {
                            str.Append(" + ").Append(values.Count - Helmet.MARKERS_MAX_SELECTED).Append(" more beyond ");
                            MyValueFormatter.AppendDistanceInBestUnit(kv.Key, str);
                            str.Append("...");
                            break;
                        }

                        str.AppendLine(kv.Value);
                    }

                    selected.Clear();

                    var text = str.ToString();

                    if(!text.Equals(prevText))
                    {
                        prevText = text;

                        var lcd = (IMyTextPanel)Entity;
                        lcd.WritePublicText(text, false);
                        lcd.ShowPublicTextOnScreen();
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_GravityGenerator), useEntityUpdate: false)]
    public class GravityGeneratorFlat : GravityGeneratorLogic { }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_GravityGeneratorSphere), useEntityUpdate: false)]
    public class GravityGeneratorSphere : GravityGeneratorLogic { }

    public class GravityGeneratorLogic : MyGameLogicComponent
    {
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
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
    }

    // TODO feature: lights
    /*
    // test if this actually works!
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Character), false)]
    public class Character : MyGameLogicComponent
    {
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void Close()
        {
            try
            {
                Helmet.characterEntities.Remove(Entity.EntityId);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            NeedsUpdate = MyEntityUpdateEnum.NONE; // HACK required until the component removes it itself
        }

        public override void UpdateOnceBeforeFrame()
        {
            try
            {
                var character = Entity as IMyCharacter;

                if(character.IsPlayer)
                {
                    Helmet.characterEntities.Add(character.EntityId, new Helmet.CharacterLightData(character));
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }
    */

    public static class Extensions
    {
        public static string ToUpperFirst(this string s)
        {
            if(string.IsNullOrEmpty(s))
                return string.Empty;

            char[] a = s.ToCharArray();
            a[0] = char.ToUpper(a[0]);
            return new string(a);
        }

        public static MyInventoryBase GetInventory(this IMyEntity entity, int index = 0)
        {
            var ent = entity as MyEntity;
            return (ent.HasInventory ? ent.GetInventoryBase(index) : null);
        }

        public static StringBuilder AppendRGBA(this StringBuilder str, Color color)
        {
            return str.Append(color.R).Append(", ").Append(color.G).Append(", ").Append(color.B).Append(", ").Append(color.A);
        }

        public static StringBuilder AppendRGB(this StringBuilder str, Color color)
        {
            return str.Append(color.R).Append(", ").Append(color.G).Append(", ").Append(color.B);
        }

        public static MyRelationsBetweenPlayers GetRelationsBetweenPlayers(this IMyPlayer player1, long playeIdentity2)
        {
            if(player1.IdentityId == playeIdentity2)
                return MyRelationsBetweenPlayers.Self;

            var faction1 = MyAPIGateway.Session.Factions.TryGetPlayerFaction(player1.IdentityId);
            var faction2 = MyAPIGateway.Session.Factions.TryGetPlayerFaction(playeIdentity2);

            if(faction1 == null || faction2 == null)
                return MyRelationsBetweenPlayers.Enemies;

            if(faction1 == faction2)
                return MyRelationsBetweenPlayers.Allies;

            if(MyAPIGateway.Session.Factions.GetRelationBetweenFactions(faction1.FactionId, faction2.FactionId) == MyRelationsBetweenFactions.Neutral)
                return MyRelationsBetweenPlayers.Neutral;

            return MyRelationsBetweenPlayers.Enemies;
        }

        // HACK temporary workaround to my matrix being weirdly aligned
        public static void TempAddBillboardOriented(MyStringId material, Vector4 color, Vector3D origin, MatrixD matrix, float radius)
        {
            MyTransparentGeometry.AddBillboardOriented(material, color, origin, matrix.Left, matrix.Down, radius);
        }
    }

    // Got from: http://stackoverflow.com/questions/5716423/c-sharp-sortable-collection-which-allows-duplicate-keys
    /// <summary>
    /// Comparer for comparing two keys, handling equality as beeing greater
    /// Use this Comparer e.g. with SortedLists or SortedDictionaries, that don't allow duplicate keys
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    public class DuplicateKeyComparer<TKey> : IComparer<TKey> where TKey : IComparable
    {
        public int Compare(TKey x, TKey y)
        {
            int result = x.CompareTo(y);
            return (result == 0 ? 1 : result);
        }
    }
}
