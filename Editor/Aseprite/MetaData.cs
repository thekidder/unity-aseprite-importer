using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Aseprite.Chunks;
using UnityEngine;

namespace Aseprite
{
    public enum MetaDataType { UNKNOWN, TRANSFORM, SECONDARY_TEXTURE };

    public class MetaData
    {
        static public string MetaDataChar = "@";

        public MetaDataType Type { get; private set; }
        
        public LayerChunk Layer { get; private set; }
        
        public int LayerIndex { get; private set; }
        
        //Average position per frames
        public Dictionary<int, Vector2> Transforms { get; private set; }
        public List<string> Args { get; private set; }

        public MetaData(LayerChunk layer, int layerIndex)
        {
            Layer = layer;
            LayerIndex = layerIndex;
            var layerName = layer.LayerName;
            Args = new List<string>();
            Transforms = new Dictionary<int, Vector2>();
            
            // Check if it's a transform layer
            var regex = new Regex("@transform\\(\"(.*)\"\\)");
            var match = regex.Match(layerName);
            if (match.Success)
            {
                Type = MetaDataType.TRANSFORM;
                Args.Add(match.Groups[1].Value);
                return;
            }
            
            // Check if secondary texture layer
            regex = new Regex("@secondary\\(\"(.*)\"\\)");
            match = regex.Match(layerName);
            if (match.Success)
            {
                Type = MetaDataType.SECONDARY_TEXTURE;
                Args.Add(match.Groups[1].Value);
                return;
            }
            
            // Check if it's a shortcut for some common secondary textures
            if (layerName.Equals("@emission", StringComparison.OrdinalIgnoreCase))
            {
                Type = MetaDataType.SECONDARY_TEXTURE;
                Args.Add("_Emission");
                return;
            }
            if (layerName.Equals("@normal", StringComparison.OrdinalIgnoreCase))
            {
                Type = MetaDataType.SECONDARY_TEXTURE;
                Args.Add("_NormalMap");
                return;
            }
            
            // Unknown metadata layer
            Debug.LogWarning($"Unsupported aseprite metadata {layerName}");
        }
    }
}
