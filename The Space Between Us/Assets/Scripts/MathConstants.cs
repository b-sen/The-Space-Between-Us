using System.Collections;
using System.Collections.Generic;
using UnityEngine;


// Constants used for mathematical purposes.
public static class MathConstants
{
    // Mathf.Epsilon is too small for errors possibly accumulating over multiple operations
    public static readonly float floatTolerance = 0.0001f;
}
