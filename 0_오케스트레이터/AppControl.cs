using System;
using VisualComponents.Create3D;

namespace DT_KETI
{
    public class AppControl
    {
        public ISimComponent[] LoadModel(IApplication app, ISimWorld simWorld, string modelPath)
        {
            simWorld.Clear();
            System.Uri modelUri = new System.Uri(modelPath);
            ISimComponent[] elements = app.LoadLayout(modelUri);
            return elements;
        }

        public void SaveModel(IApplication app, string outputPath)
        {
            System.Uri modelUri = new System.Uri(outputPath);
            app.SaveLayout(modelUri);
        }
    }
}
