using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.ObjectBuilders;
using VRage.Game.Components;
using VRage.ModAPI;

namespace Digi.Helmet
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Welder), true)]
    public class Welder : Item { }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_AngleGrinder), true)]
    public class Grinder : Item { }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_HandDrill), true)]
    public class Drill : Item { }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_AutomaticRifle), true)]
    public class Rifle : Item { }

    public class Item : MyGameLogicComponent
    {
        private MyObjectBuilder_EntityBase obj;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            obj = objectBuilder;
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }
        
        public override void UpdateOnceBeforeFrame()
        {
            try
            {
                if(MyAPIGateway.Session == null || MyAPIGateway.Session.Player == null || MyAPIGateway.Session.Player.Character == null)
                    return;

                var equipped = MyAPIGateway.Session.Player.Character.EquippedTool;

                if(Entity == equipped)
                {
                    Helmet.holdingTool = Entity;
                    Helmet.holdingToolTypeId = obj.TypeId;
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