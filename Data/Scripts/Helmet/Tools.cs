using System;
using System.Linq;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Game.Components;
using VRage.ModAPI;

namespace Digi.Helmet
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Welder), false)]
    public class Welder : Item { }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_AngleGrinder), false)]
    public class Grinder : Item { }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_HandDrill), false)]
    public class Drill : Item { }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_AutomaticRifle), false)]
    public class Rifle : Item { }

    public class Item : MyGameLogicComponent
    {
        private MyObjectBuilder_EntityBase obj;
        
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            obj = objectBuilder;
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void Close()
        {
            NeedsUpdate = MyEntityUpdateEnum.NONE; // HACK required until the component removes it itself
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
                        Helmet.holdingToolTypeId = obj.TypeId;
                    }
                }
                
                obj = null;
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }
}