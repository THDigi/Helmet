﻿using System;
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
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Common.Utils;
using VRageMath;
using VRage;
using VRage.ObjectBuilders;
using VRage.Components;
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
            Helmet.holdingTool = Entity;
            Helmet.holdingToolTypeId = objectBuilder.TypeId.ToString();
            Helmet.holdingToolSubtypeId = objectBuilder.SubtypeName;
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