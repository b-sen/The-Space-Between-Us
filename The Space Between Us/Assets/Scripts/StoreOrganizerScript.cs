using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

using UnityEngine;

using NavGraph;

/*
 * This script handles the store layout and the navigational graph based on 
 * that layout.  The navigational graph is used for NPC pathfinding, to make 
 * it both realistic and tractable.
 */
public class StoreOrganizerScript : MonoBehaviour
{
    // Inspector parameters controlling various aspects of the store layout.
    // All are measured in meters, corresponding to game units in the scene.
    // ASSUMES that storeArea contains all other areas.
    // ASSUMES that all other areas do not overlap with each other.
    // ASSUMES that the store is laid out as
    //                                       staff area
    //                                       aisles
    //                                       checkouts | entrance lobby
    // for now.

    // entire store, including employee-only areas
    public Rect storeArea;

    // block of aisles holding products; aligned vertically for ease of layout
    public Rect aisleArea;
    public float aisleShelfWidth;  // recall that two aisles are back-to-back except at the edges
    public float aisleShelfHeight;
    public float aislePairSpace;  // distance between two aisles facing each other
    public float aisleTopSpace;  // between top of aisle area and top aisle row
    public float aisleMidSpace;  // between rows of aisles (only used if multiple rows used)
    public float aisleBottomSpace;  // between bottom of aisle area and bottom aisle row
    public int maxAisleRows;  // provided to limit navigation graph size
    public int maxAislePairColumns;  // likewise

    // checkout lanes, largely analogous to aisles but without back-to-back, pairs, or multiple rows
    public Rect checkoutArea;
    public float checkoutWidth;
    public float checkoutHeight;
    public float checkoutBetweenSpace;
    public float checkoutTopSpace;
    public float checkoutBottomSpace;
    public float maxCheckoutLanes;

    // entrances represented as infinitely thin rectangles for flexibility, and manually paired with their "lobbies" for now
    public List<Rect> entrances;  // currently only the first entrance and its lobby are used
    public List<Rect> lobbies;  // must be in same order as corresponding entrances!

    // staff-only area (for holding stock)
    public Rect staffArea;


    // Inspector Prefabs to display the store with.  These are scaled BY THIS
    // SCRIPT assuming a 1m x 1m base and origin at bottom-left.
    public GameObject floor;
    public GameObject aisleShelf;
    public GameObject checkout;
    public GameObject staffFloor;


    // Generated items based on the store layout parameters.
    // Arrays and HashSets over Lists for performance, as these will be generated once and then accessed frequently.
    private int numAisleRows;
    private int numAislePairColumns;
    private GameObject[,] aisles;
    private int numCheckouts;
    private GameObject[] checkouts;
    private HashSet<Zone> allShopperZones;  // the whole customer-accessible graph, in case of "eep I'm lost"
    private Zone outside;  // special zone where customers come from and return to
    private Zone lobby;
    private Zone checkoutTop;
    private Zone checkoutBottom;
    private Zone[] checkoutLanes;
    private Zone aisleTop;
    private Zone aisleBottom;
    private Zone[] aisleMids;
    private Zone[,] aislePairs;


    // Initialize the store layout and make the navigational graph.
    void Start()
    {
        // Place down the visible objects.  Build the navigational graph around 
        // the aisles and checkouts as they are placed.

        // position and then scale, we don't need to keep a reference around for these
        Instantiate(floor, storeArea.position, Quaternion.identity).transform.localScale = storeArea.size;
        Instantiate(staffFloor, staffArea.position, Quaternion.identity).transform.localScale = staffArea.size;

        // the aisles and checkouts DO need references kept, and also need to be tiled

        // checkouts first since they are simpler and illustrate the principles for aisles
        UnityEngine.Debug.Assert((checkoutArea.height + MathConstants.floatTolerance) >= (checkoutTopSpace + checkoutHeight + checkoutBottomSpace), "Config ERROR: Checkout area height too small!");
        numCheckouts = (int)Math.Min(Math.Floor((checkoutArea.width + MathConstants.floatTolerance) / (checkoutBetweenSpace + checkoutWidth)),  // each checkout and its lane beside it
            maxCheckoutLanes);
        UnityEngine.Debug.Assert((numCheckouts > 0), "Config ERROR: Checkout area width too small!");

        float checkoutYPosition = checkoutArea.y + checkoutBottomSpace;  // remember, Y increases upwards so it is convenient to start from the bottom and work upwards
        checkouts = new GameObject[numCheckouts];  // automatically initializes to null
        checkoutLanes = new Zone[numCheckouts];  // likewise
        for (int currentCheckout = 0; currentCheckout < numCheckouts; currentCheckout++)  // for each checkout in the row
        {
            float laneXPosition = checkoutArea.x + currentCheckout * (checkoutBetweenSpace + checkoutWidth);  // shift over by the number of preexisting lanes
            checkoutLanes[currentCheckout] = new Zone("checkout lane " + currentCheckout.ToString(), new Rect(laneXPosition, checkoutYPosition, checkoutBetweenSpace, checkoutHeight)); // add lane to navigational graph
            float checkoutXPosition = laneXPosition + checkoutBetweenSpace;
            checkouts[currentCheckout] = Instantiate(checkout, new Vector2(checkoutXPosition, checkoutYPosition), Quaternion.identity);
            checkouts[currentCheckout].transform.localScale = new Vector2(checkoutWidth, checkoutHeight);
        }

        // add top and bottom checkout zones to navigational graph
        checkoutBottom = new Zone("zone below checkouts", new Rect(checkoutArea.x, checkoutArea.y, checkoutArea.width, checkoutBottomSpace));
        checkoutTop = new Zone("zone above checkouts", new Rect(checkoutArea.x, checkoutYPosition + checkoutHeight, checkoutArea.width, checkoutArea.height - checkoutHeight - checkoutBottomSpace));
        
        // add edges within checkout area to navigational graph
        foreach (Zone lane in checkoutLanes)
        {
            // each checkout lane joins to the top and bottom zones and nowhere else
            Rect topEdge = new Rect(lane.area.x, lane.area.yMax, lane.area.width, 0);
            Rect bottomEdge = new Rect(lane.area.x, lane.area.yMin, lane.area.width, 0);
            Zone.AddEdgeBetweenZones(topEdge, lane, checkoutTop);
            Zone.AddEdgeBetweenZones(bottomEdge, lane, checkoutBottom);
        }
        // all other edges attached to the checkout top and bottom are in other regions, so will be added further down when regions are connected

        // aisles follow the same principles as checkouts, but can have multiple rows (we build one row at a time)
        UnityEngine.Debug.Assert((aisleArea.height + MathConstants.floatTolerance) >= (aisleTopSpace + aisleShelfHeight + aisleBottomSpace), "Config ERROR: Aisle area height too small!");
        numAisleRows = (int)Math.Min(Math.Floor((aisleArea.height + MathConstants.floatTolerance - aisleTopSpace - aisleBottomSpace + aisleMidSpace) /  // 1 less middle space than we have rows
            (aisleShelfHeight + aisleMidSpace)), maxAisleRows);
        numAislePairColumns = (int)Math.Min(Math.Floor((aisleArea.width + MathConstants.floatTolerance) / (aislePairSpace + 2 * aisleShelfWidth)),  // 2 shelves in each facing pair
            maxAislePairColumns);
        UnityEngine.Debug.Assert((numAislePairColumns > 0), "Config ERROR: Aisle area width too small!");

        aisleBottom = new Zone("zone below aisles", new Rect(aisleArea.x, aisleArea.y, aisleArea.width, aisleBottomSpace));  // add bottom aisle zone to navigational graph
        aisles = new GameObject[numAisleRows, 2 * numAislePairColumns];  // automatically initializes to null
        aislePairs = new Zone[numAisleRows, numAislePairColumns];  // likewise
        aisleMids = new Zone[numAisleRows - 1];  // as with row calculation, 1 less middle space than we have rows
        float aisleRowYPosition = aisleArea.y + aisleBottomSpace;  // remember, Y increases upwards
        for (int currentAisleRow = 0; currentAisleRow < numAisleRows; currentAisleRow++)  // for each row of aisles
        {
            for (int currentAislePairColumn = 0; currentAislePairColumn < numAislePairColumns; currentAislePairColumn++)  // for each pair within this row
            {
                float pairXPosition = aisleArea.x + currentAislePairColumn * (aislePairSpace + 2 * aisleShelfWidth);  // shift over by the number of preexisting pairs
                // left side of pair
                aisles[currentAisleRow, 2 * currentAislePairColumn] = Instantiate(aisleShelf, new Vector2(pairXPosition, aisleRowYPosition), Quaternion.identity);
                aisles[currentAisleRow, 2 * currentAislePairColumn].transform.localScale = new Vector2(aisleShelfWidth, aisleShelfHeight);
                // add space between pairs to navigational graph
                aislePairs[currentAisleRow, currentAislePairColumn] = new Zone("aisle pair " + currentAislePairColumn.ToString() + " in row " + currentAisleRow.ToString(), 
                    new Rect(pairXPosition + aisleShelfWidth, aisleRowYPosition, aislePairSpace, aisleShelfHeight));
                // right side of pair
                aisles[currentAisleRow, 2 * currentAislePairColumn + 1] = 
                    Instantiate(aisleShelf, new Vector2(pairXPosition + aisleShelfWidth + aislePairSpace, aisleRowYPosition), Quaternion.identity);
                aisles[currentAisleRow, 2 * currentAislePairColumn + 1].transform.localScale = new Vector2(aisleShelfWidth, aisleShelfHeight);
            }

            bool isLastRow = (currentAisleRow == (numAisleRows - 1));

            if (isLastRow)
            {
                // if this row is the last, add the top space to the navigational graph
                aisleTop = new Zone("zone above aisles", new Rect(aisleArea.x, aisleRowYPosition + aisleShelfHeight, aisleArea.width, aisleArea.yMax - (aisleRowYPosition + aisleShelfHeight)));
            } else
            {
                // if this row is not the last, add the middle space to the navigational graph
                aisleMids[currentAisleRow] = new Zone("middle zone above aisle row" + currentAisleRow.ToString(), 
                    new Rect(aisleArea.x, aisleRowYPosition + aisleShelfHeight, aisleArea.width, aisleMidSpace));
            }

            bool isFirstRow = (currentAisleRow == 0);
            // add edges between this row of aisles and other zones in the navigational graph
            for (int currentAislePairColumn = 0; currentAislePairColumn < numAislePairColumns; currentAislePairColumn++)  // can't naturally foreach just one row
            {
                Zone aislePair = aislePairs[currentAisleRow, currentAislePairColumn];
                // each space between a pair of aisles joins to the zones below and above and nowhere else
                Rect topEdge = new Rect(aislePair.area.x, aislePair.area.yMax, aislePair.area.width, 0);
                Rect bottomEdge = new Rect(aislePair.area.x, aislePair.area.yMin, aislePair.area.width, 0);
                if (isLastRow) { Zone.AddEdgeBetweenZones(topEdge, aislePair, aisleTop); } 
                else { Zone.AddEdgeBetweenZones(topEdge, aislePair, aisleMids[currentAisleRow]); }
                if (isFirstRow) { Zone.AddEdgeBetweenZones(bottomEdge, aislePair, aisleBottom); }
                else { Zone.AddEdgeBetweenZones(bottomEdge, aislePair, aisleMids[currentAisleRow - 1]); }  // the space between rows that the LAST row added above it
            }

            aisleRowYPosition += aisleShelfHeight + aisleMidSpace;  // bump Y upwards for next row
        }
        
        lobby = new Zone("lobby", lobbies[0]);  // add lobby to navigational graph, conveniently we already know where it is
        outside = new Zone("outside", new Rect(lobbies[0].x, lobbies[0].y - lobbies[0].height, lobbies[0].width, lobbies[0].height)); // add outside to navigational graph, taking size of lobby but moved down

        // connect up regions of navigational graph 
        Zone.AddEdgeBetweenZones(entrances[0], lobby, outside);  // entrance connects lobby and outside, and is the only way out
        // to the left of the lobby is the checkouts, adjacent to the top and bottom but not necessarily a lane
        Rect checkoutTopRightEdge = new Rect(checkoutTop.area.xMax, checkoutTop.area.y, 0, checkoutTop.area.height);
        Rect checkoutBottomRightEdge = new Rect(checkoutBottom.area.xMax, checkoutBottom.area.y, 0, checkoutBottom.area.height);
        Zone.AddEdgeBetweenZones(checkoutTopRightEdge, checkoutTop, lobby);
        Zone.AddEdgeBetweenZones(checkoutBottomRightEdge, checkoutBottom, lobby);
        // above the lobby is the bottom of the aisles
        Rect lobbyTopEdge = new Rect(lobby.area.x, lobby.area.yMax, lobby.area.width, 0);
        Zone.AddEdgeBetweenZones(lobbyTopEdge, aisleBottom, lobby);
        // above the checkouts is also the bottom of the aisles
        Rect checkoutTopTopEdge = new Rect(checkoutTop.area.x, checkoutTop.area.yMax, checkoutTop.area.width, 0);
        Zone.AddEdgeBetweenZones(checkoutTopTopEdge, aisleBottom, checkoutTop);

        // add all regions to HashSet
        allShopperZones = new HashSet<Zone>();
        allShopperZones.Add(outside);
        allShopperZones.Add(lobby);
        allShopperZones.Add(checkoutTop);
        allShopperZones.Add(checkoutBottom);
        allShopperZones.UnionWith(checkoutLanes);  // iterates over automatically
        allShopperZones.Add(aisleTop);
        allShopperZones.Add(aisleBottom);
        allShopperZones.UnionWith(aisleMids);  // iterates over automatically
        foreach (Zone aislePair in aislePairs)  // automatic iteration handles multidimensional arrays properly, but sadly UnionWith doesn't iterate over multidimensional arrays
        {
            allShopperZones.Add(aislePair);
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
