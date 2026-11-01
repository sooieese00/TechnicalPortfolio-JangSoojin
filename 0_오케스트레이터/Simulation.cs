using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisualComponents.Create3D;

namespace DT_KETI
{
    public class Simulation
    {
        public void ExecuteSimulation(IApplication app, double horizon, double speedFactor)
        {
            app.Simulation.SpeedFactor = speedFactor;
            app.Simulation.Update();
            app.Simulation.Run(horizon);
        }
    }
}
