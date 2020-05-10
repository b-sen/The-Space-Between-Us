using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/* A "traditional" graph for navigating an area without being stuck on roads 
 * or other fixed routes would tile the area tightly with nodes, producing a 
 * large graph with small distances between nodes to search over.  This is 
 * both computationally expensive to pathfind over and unrealistic to how we 
 * actually think about navigating store environments.  We don't think in 
 * small fractions of steps, we think "the milk is in aisle 6 so first go to 
 * aisle 6."  This also contributes to our flexibility; if someone steps in 
 * front of us we don't have to redo the higher-level path planning to decide 
 * how to move around them.
 * 
 * Accordingly, for this purpose we build our understanding of the store's 
 * topology into the graph.  Rather than having each vertex be a point in the 
 * store, each vertex is a zone such as "the space between aisles 10 and 11" 
 * or "the space between the aisles and the north store wall."  This provides 
 * a much smaller graph that is reflective of how we consider and chunk areas
 * - invalid locations such as "inside the shelves" are not even considered!
 * 
 * Because each vertex is a zone, the edges between them are very literally 
 * edges; not always an entire face, but edges nonetheless.  A single zone 
 * may have edges to many other zones.  
 * 
 * For our purposes, we do not need to iterate over all edges in the graph, 
 * but we frequently need to iterate over all edges attached to a zone (when 
 * considering where an NPC might go from that zone).  Therefore, we have each 
 * zone track the edges attached to it instead of having a global edge list or 
 * matrix.  This produces a data duplication - each edge is represented by 
 * both its attached zones - but since the graph is built once in 
 * StoreOrganizer and both representations of each edge are added together, 
 * the impact on code cleanliness is limited.
 */

/// <summary>
/// Definitions specifically for use with the navigational graph.
/// </summary>
namespace NavGraph
{
    public class Zone
    {
        public string name;  // for debug and understanding purposes
        public Rect area;  // the area within this zone
        // would have access restrictions but deep copying isn't provided by C# and this needs to be iterated over; ONLY modify via methods in this namespace and only to build the graph
        public Dictionary<Zone, Rect> edges;

        public Zone(string zoneName, Rect zoneArea)
        {
            name = zoneName;
            area = zoneArea;
            edges = new Dictionary<Zone, Rect>();  // add edges after
        }

        // Adds the edge to both zones, both for convenience and to limit the 
        // impact of the data duplication in edge representation.
        public static void AddEdgeBetweenZones(Rect edge, Zone zone1, Zone zone2)
        {
            zone1.edges.Add(zone2, edge);
            zone2.edges.Add(zone1, edge);
        }
    }
}
