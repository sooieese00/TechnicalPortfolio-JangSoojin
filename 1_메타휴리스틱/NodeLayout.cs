using System;

namespace PDP_ASRS_WIP
{
    public sealed class NodeLayout
    {
        public int Stations { get; }
        public int Count { get; }
        public int AsrsOut { get; }
        public int AsrsIn { get; }
        public int Origin { get; }

        readonly bool split;
        NodeLayout(int stations, bool split)
        {
            Stations = stations;
            this.split = split;
            if (split)
            {
                Count = 2 * stations + 3;
                AsrsOut = 2 * stations;
                AsrsIn = 2 * stations + 1;
                Origin = 2 * stations + 2;
            }
            else
            {
                Count = stations + 2;
                AsrsOut = stations;
                AsrsIn = stations;
                Origin = stations + 1;
            }
        }

        public static NodeLayout Compact(int stations) => new NodeLayout(stations, false);
        public static NodeLayout Split(int stations) => new NodeLayout(stations, true);
        public int In(int k) => k;
        public int Out(int k) => split ? Stations + k : k;
        public int StationOf(int node)
        {
            if (node < 0)
            {
                return -1;
            }

            if (node < Stations)
            {
                return node;
            }

            if (split && node < 2 * Stations)
            {
                return node - Stations;
            }

            return -1;
        }

        public string Describe(int node)
        {
            if (node == Origin)
            {
                return "ORIGIN";
            }

            if (node == AsrsOut)
            {
                return AsrsOut == AsrsIn ? "ASRS" : "ASRS_OUT";
            }

            if (node == AsrsIn)
            {
                return "ASRS_IN";
            }

            int k = StationOf(node);
            if (k < 0)
            {
                return "node" + Convert.ToString(node);
            }

            return (split ? (node < Stations ? "IN" : "OUT") + " " : "") + "WS" + Convert.ToString(k);
        }
    }
}
