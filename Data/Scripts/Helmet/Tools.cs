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
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Common.Utils;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;
using VRage;
using VRage.ObjectBuilders;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.Utils;
using Digi.Utils;

namespace Digi.Helmet
{
    public class Item : MyGameLogicComponent
    {
        private MyObjectBuilder_EntityBase objectBuilder;
        
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            this.objectBuilder = objectBuilder;
            Entity.NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }
        
        public override void UpdateOnceBeforeFrame()
        {
            try
            {
                if(MyAPIGateway.Session.ControlledObject != null && MyAPIGateway.Session.ControlledObject.Entity is IMyCharacter)
                {
                    var charEnt = MyAPIGateway.Session.ControlledObject.Entity;
                    var charObj = charEnt.GetObjectBuilder(false) as MyObjectBuilder_Character;
                    
                    if(charObj.HandWeapon != null && Entity.EntityId == charObj.HandWeapon.EntityId)
                    {
                        Helmet.holdingTool = Entity;
                        Helmet.holdingToolTypeId = objectBuilder.TypeId;
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return copy ? (MyObjectBuilder_EntityBase)objectBuilder.Clone() : objectBuilder;
        }
    }
    
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Welder))]
    public class Welder : Item { }
    
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_AngleGrinder))]
    public class Grinder : Item { }
    
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_HandDrill))]
    public class Drill : Item { }
    
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_AutomaticRifle))]
    public class Weapon : Item { }
}