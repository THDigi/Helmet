using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Input;

namespace Digi
{
    public static class Dev
    {
        private static Dictionary<string, double> values = new Dictionary<string, double>();

        public static double GetValueScroll(string id, double initial, double step, MyKeys modifier = MyKeys.None, int roundDigits = 10, int notifyTime = 16)
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

                if(roundDigits > -1)
                    val = Math.Round(val, roundDigits);

                values[id] = val;
            }

            MyAPIGateway.Utilities.ShowNotification(id + "=" + val.ToString("#,###,###,##0.##########"), notifyTime);
            return val;
        }
    }
}