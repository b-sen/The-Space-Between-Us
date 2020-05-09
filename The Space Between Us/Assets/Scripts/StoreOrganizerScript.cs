using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

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
    private int numAisleRows;
    private int numAislePairColumns;
    private GameObject[,] aisles;
    private int numCheckouts;
    private GameObject[] checkouts;


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

        float checkoutYPosition = checkoutArea.y + checkoutBottomSpace;  // remember, Y increases upwards
        checkouts = new GameObject[numCheckouts];  // automatically initializes to null
        for (int currentCheckout = 0; currentCheckout < numCheckouts; currentCheckout++)  // for each checkout in the row
        {
            float laneXPosition = checkoutArea.x + currentCheckout * (checkoutBetweenSpace + checkoutWidth);  // shift over by the number of preexisting lanes
            // TODO: add lane to navigational graph
            float checkoutXPosition = laneXPosition + checkoutBetweenSpace;
            checkouts[currentCheckout] = Instantiate(checkout, new Vector2(checkoutXPosition, checkoutYPosition), Quaternion.identity);
            checkouts[currentCheckout].transform.localScale = new Vector2(checkoutWidth, checkoutHeight);
        }
        // TODO: add top and bottom checkout zones to navigational graph
        // TODO: add edges within checkout area to navigational graph

        // aisles follow the same principles as checkouts, but can have multiple rows (we build one row at a time)
        UnityEngine.Debug.Assert((aisleArea.height + MathConstants.floatTolerance) >= (aisleTopSpace + aisleShelfHeight + aisleBottomSpace), "Config ERROR: Aisle area height too small!");
        numAisleRows = (int)Math.Min(Math.Floor((aisleArea.height + MathConstants.floatTolerance - aisleTopSpace - aisleBottomSpace + aisleMidSpace) /  // 1 less middle space than we have rows
            (aisleShelfHeight + aisleMidSpace)), maxAisleRows);
        numAislePairColumns = (int)Math.Min(Math.Floor((aisleArea.width + MathConstants.floatTolerance) / (aislePairSpace + 2 * aisleShelfWidth)),  // 2 shelves in each facing pair
            maxAislePairColumns);
        UnityEngine.Debug.Assert((numAislePairColumns > 0), "Config ERROR: Aisle area width too small!");

        // TODO: add bottom aisle zone to navigational graph
        aisles = new GameObject[numAisleRows, 2 * numAislePairColumns];  // automatically initializes to null
        float aisleRowYPosition = aisleArea.y + aisleBottomSpace;  // remember, Y increases upwards
        for (int currentAisleRow = 0; currentAisleRow < numAisleRows; currentAisleRow++)  // for each row of aisles
        {
            for (int currentAislePairColumn = 0; currentAislePairColumn < numAislePairColumns; currentAislePairColumn++)  // for each pair within this row
            {
                float pairXPosition = aisleArea.x + currentAislePairColumn * (aislePairSpace + 2 * aisleShelfWidth);  // shift over by the number of preexisting pairs
                // left side of pair
                aisles[currentAisleRow, 2 * currentAislePairColumn] = Instantiate(aisleShelf, new Vector2(pairXPosition, aisleRowYPosition), Quaternion.identity);
                aisles[currentAisleRow, 2 * currentAislePairColumn].transform.localScale = new Vector2(aisleShelfWidth, aisleShelfHeight);
                // TODO: add space between pairs to navigational graph
                // right side of pair
                aisles[currentAisleRow, 2 * currentAislePairColumn + 1] = 
                    Instantiate(aisleShelf, new Vector2(pairXPosition + aisleShelfWidth + aislePairSpace, aisleRowYPosition), Quaternion.identity);
                aisles[currentAisleRow, 2 * currentAislePairColumn + 1].transform.localScale = new Vector2(aisleShelfWidth, aisleShelfHeight);
            }

            // TODO: add edges between this row of aisles and the space below in the navigational graph
            // TODO: if this row is not the last, add the middle space to the navigational graph
            // TODO: if this row is the last, add the top space to the navigational graph
            // TODO: add edges between this row of aisles and the space above in the navigational graph

            aisleRowYPosition += aisleShelfHeight + aisleMidSpace;  // bump Y upwards for next row
        }

        // TODO: add entrance and lobby to navigational graph
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
