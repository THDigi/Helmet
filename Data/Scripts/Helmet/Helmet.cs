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
using Sandbox.ModAPI;
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

namespace Digi.Helmet
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class Helmet : MySessionComponentBase
    {
        // TODO: finish 2nd helmet model
        // TODO: helmet on/off animation?
        
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
        
        private float oldFov = 60;
        private int slowUpdateFov = 0;
        
        public static List<IMyGravityGeneratorBase> gravityGenerators = new List<IMyGravityGeneratorBase>();
        private Vector3 gravityDir = Vector3.Zero;
        private bool prevGravityDir = false;
        private int slowUpdateGravDir = 0;
        
        private MatrixD helmetMatrix;
        private float[] values = new float[Settings.TOTAL_ELEMENTS];
        private bool[] show = new bool[Settings.TOTAL_ELEMENTS];
        private MatrixD lastMatrix;
        //private MatrixD temp;
        //private Vector3D pos;
        private int lastOxygenEnv = 0;
        private bool fatalError = false;
        
        private MyObjectBuilder_Character characterObject;
        private int slowUpdateCharObj = 0;
        
        public IMyEntity[] iconEntities = new IMyEntity[Settings.TOTAL_ELEMENTS];
        public IMyEntity[] iconBarEntities = new IMyEntity[Settings.TOTAL_ELEMENTS];
        
        private const int HUD_BAR_MAX_SCALE = 380;
        private const float SCALE_DIST_ADJUST = 0.1f;
        private const float HUDSCALE_DIST_ADJUST = 0.1f;
        
        private const string CUBE_HELMET_PREFIX = "Helmet_";
        private const string CUBE_HUD_PREFIX = "HelmetHUD_";
        private const string MOD_NAME = "Helmet";
        
        private const string HELP_COMMANDS =
            "/helmet for fov [number]   number is optional, quickly set scales for a specified FOV value\n" +
            "/helmet <on/off>   turn the entire mod on or off\n" +
            "/helmet scale <number>   -1.0 to 1.0, default 0\n" +
            "/helmet hud <on/off>   turn the HUD component on or off\n" +
            "/helmet hud scale <number>   -1.0 to 1.0, default 0\n" +
            "/helmet reload   re-loads the config file (for advanced editing)\n" +
            "\n" +
            "For advanced editing go to:\n" +
            "%appdata%\\SpaceEngineers\\Storage\\428842256_Helmet\\helmet.cfg";
        
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
            
            if(characterEntity != null && settings.autoFovScale)
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
            var controllable = characterEntity as IMyControllableEntity;
            
            if(controllable != null && controllable.EnabledHelmet)
            {
                // ignoring head Y axis on foot because of an issue with ALT and 3rd person camera bumping into the ground
                bool inCockpit = (MyAPIGateway.Session.ControlledObject.Entity is IMyCockpit);
                UpdateHelmetAt(MyAPIGateway.Session.ControlledObject.GetHeadMatrix(inCockpit, true));
                return true;
            }
            
            return false;
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
            
            for(int id = 0; id < Settings.TOTAL_ELEMENTS; id++)
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
        
        double lastLinearSpeed;
        Vector3D lastDirSpeed;
        
        private void UpdateHelmetAt(MatrixD matrix)
        {
            try
            {
                if(fatalError)
                    return; // stop trying if a fatal error occurs
                
                bool slowUpdate = false; // slow update stats later on
                
                // Update the character object slowly
                if(++slowUpdateCharObj % 10 == 0)
                {
                    slowUpdateCharObj = 0;
                    slowUpdate = true;
                    characterObject = characterEntity.GetObjectBuilder(false) as MyObjectBuilder_Character;
                }
                
                // Spawn the helmet model if it's not spawned
                if(helmet == null && settings.helmetModel != null)
                {
                    helmet = SpawnPrefab(CUBE_HELMET_PREFIX + settings.helmetModel + (settings.glass ? "" : "NoReflection"));
                    
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
                        
                        double lenDiff = Math.Abs((lastMatrix.Forward - matrix.Forward).Length()) * 4 * settings.delayedRotation;
                        double amount = Math.Min(Math.Max(lenDiff, 0.25), 1.0);
                        
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
                if(!settings.hud || characterObject == null)
                {
                    RemoveHud();
                    return;
                }
                
                if(slowUpdate)
                {
                    // show and value cache for the HUD elements
                    for(int id = 0; id < Settings.TOTAL_ELEMENTS; id++)
                    {
                        values[id] = 0;
                        show[id] = settings.elements[id].show;
                    }
                    
                    show[Icons.BROADCASTING] = characterObject.EnableBroadcasting;
                    show[Icons.DAMPENERS] = characterObject.DampenersEnabled;
                    
                    values[Icons.HEALTH] = (characterEntity is IMyDestroyableObject ? (characterEntity as IMyDestroyableObject).Integrity : 100);
                    values[Icons.ENERGY] = (characterObject.Battery != null ? characterObject.Battery.CurrentCapacity * 10000000 : 0);
                    values[Icons.OXYGEN] = characterObject.OxygenLevel * 100;
                    values[Icons.OXYGEN_ENV] = (characterEntity as IMyCharacter).EnvironmentOxygenLevel * 2;
                    values[Icons.INVENTORY] = 0;
                    
                    // Get the inventory volume
                    var invOwner = (characterEntity as Sandbox.ModAPI.Interfaces.IMyInventoryOwner);
                    
                    if(invOwner != null && invOwner.InventoryCount > 0)
                    {
                        var inv = invOwner.GetInventory(0);
                        
                        if(inv != null)
                            values[Icons.INVENTORY] = ((float)inv.CurrentVolume / (float)inv.MaxVolume) * 100;
                    }
                }
                
                // Update the warning icon
                show[Icons.WARNING] = false;
                
                if(settings.elements[Icons.WARNING].show &&
                   (values[Icons.HEALTH] <= settings.elements[Icons.HEALTH].warnPercent
                    || values[Icons.ENERGY] <= settings.elements[Icons.ENERGY].warnPercent
                    || values[Icons.OXYGEN] <= settings.elements[Icons.OXYGEN].warnPercent))
                {
                    double warnTick = DateTime.UtcNow.Ticks;
                    
                    if(lastWarningBlink < warnTick)
                    {
                        warningBlinkOn = !warningBlinkOn;
                        lastWarningBlink = warnTick + (TimeSpan.TicksPerSecond * settings.warnBlinkTime);
                    }
                    
                    show[Icons.WARNING] = true;
                }
                
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
                    if(++slowUpdateGravDir % 6 == 0)
                    {
                        slowUpdateGravDir = 0;
                        
                        gravityDir = GetGravityAtPoint(characterEntity.WorldAABB.Center);
                        
                        if(gravityDir != Vector3.Zero)
                            gravityDir.Normalize();
                        
                        if(prevGravityDir == (gravityDir == Vector3.Zero))
                        {
                            if(iconEntities[id] != null)
                            {
                                iconEntities[id].Close();
                                iconEntities[id] = null;
                            }
                            
                            prevGravityDir = !prevGravityDir;
                        }
                    }
                    
                    if(gravityDir == Vector3.Zero)
                        name += "None";
                    else
                        name += "Dir";
                }
                
                // spawn the element if it's not already
                if(iconEntities[id] == null)
                {
                    iconEntities[id] = SpawnPrefab(CUBE_HUD_PREFIX + name);
                    
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
                    
                    //Vector3 dir = gravityDir;
                    //dir += Vector3.Normalize(lastMatrix.Translation - matrix.Translation) / 2;
                    //dir.Normalize();
                    
                    AlignToVector(ref matrix, gravityDir);
                    iconEntities[id].SetWorldMatrix(matrix);
                    return;
                }
                
                // align the element to the view and give it the glass curve
                TransformHUD(ref matrix, matrix.Translation + matrix.Forward * 0.05, headPos, -matrix.Up, matrix.Forward);
                iconEntities[id].SetWorldMatrix(matrix);
                
                if(!element.hasBar)
                    return; // if the HUD element has no bar, stop here.
                
                if(iconBarEntities[id] == null)
                {
                    iconBarEntities[id] = SpawnPrefab(CUBE_HUD_PREFIX + name + "Bar");
                    
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
                matrix.Translation += (align * (scale / 100.0)) - (align * (0.0097 + (0.0008 * (0.5 - (percent / 100)))));
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
                    ColorMaskHSV = PrefabVector0,
                    ShareMode = MyOwnershipShareModeEnum.None,
                    DeformationRatio = 0,
                    ShowOnHUD = false,
                }
            }
        };
        
        private IMyEntity SpawnPrefab(string name)
        {
            try
            {
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
        
        public static Vector3 GetGravityAtPoint(Vector3D point)
        {
            Vector3 gravity = Vector3.Zero;
            
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
                            }
                        }
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
            
            return gravity;
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
                    
                    if(helmet != null)
                    {
                        helmet.Close();
                        helmet = null;
                    }
                    
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