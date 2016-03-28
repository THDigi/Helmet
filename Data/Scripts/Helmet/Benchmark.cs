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
using Sandbox.ModAPI;
using VRageMath;
using VRage;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRage.Library.Utils;

namespace Digi.Utils
{
    class Benchmark
    {
        string name;
        MyGameTimer timer;
        long min = long.MaxValue;
        long max = 0;
        double avg = 0;
        long times = 0;
        
        public Benchmark(string name)
        {
            this.name = name;
        }
        
        public void Start()
        {
            timer = new MyGameTimer();
        }
        
        public void End()
        {
            long elapsed = timer.ElapsedTicks;
            min = Math.Min(min, elapsed);
            max = Math.Max(max, elapsed);
            avg = (avg + elapsed) / 2.0;
            times++;
            
            if(times % 60 == 0)
                Report();
        }
        
        public void Report()
        {
            Log.Info("BENCHMARK: avg="+TicksToMs(avg)+"ms, min="+TicksToMs(min)+"ms, max="+TicksToMs(max)+"ms");
        }
        
        private double TicksToMs(double ticks)
        {
            return TicksToMs((long)ticks);
        }
        
        private double TicksToMs(long ticks)
        {
            return Math.Round(TimeSpan.FromTicks(ticks).TotalMilliseconds, 4);
        }
    }
}