using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Gui;
using Sandbox.Game.Lights;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.Input;
using VRageMath;
using VRage;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Digi.Utils
{
    public static class Dev
    {
        private static Dictionary<string, double> values = new Dictionary<string, double>();
        
        public static double GetValueScroll(string id, double initial, double step, MyKeys modifier = MyKeys.None)
        {
            if(!values.ContainsKey(id))
                values.Add(id, initial);
            
            var val = values[id];
            
            if(modifier != MyKeys.None && !MyAPIGateway.Input.IsKeyPress(modifier))
                return val;
            
            var scroll = MyAPIGateway.Input.DeltaMouseScrollWheelValue();
            
            if(scroll != 0)
            {
                val += (scroll > 0 ? step : -step);
                values[id] = val;
            }
            
            MyAPIGateway.Utilities.ShowNotification(id+"="+val, 16);
            return val;
        }
    }
}