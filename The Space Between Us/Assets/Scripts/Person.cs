using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Security.Cryptography;
using UnityEngine;


/* The base for player character and NPC alike.
 * 
 * Abstract because it does not define how the character DECIDES their 
 * movements.  Does handle attempting those movements, via a 2D 
 * CharacterController-style system inspired by 
 * https://roystan.net/articles/character-controller-2d.html 's tutorial.
 * 
 * Also includes size and personal space, as all people have these (NPCs 
 * should try to give the PC personal space as well).
 */
public abstract class Person : MonoBehaviour
{
    // size
    protected float radius;  // people are circles, so this is our size
    protected float physicalDistance;  // how far our "personal space bubble" extends beyond our EDGE
    protected Collider2D ownCollider;  // used to avoid false alarms about colliding with oneself

    // movement
    protected float maxWalkSpeed;  // top speed moving calmly, in m/s
    protected float maxBoostSpeed;  // max additional speed for short bursts, such as running or backpedalling to avoid someone
    protected Vector2 velocity;
    protected float maxWalkAcceleration;  // in m/s^2
    protected float maxBoostAcceleration;  // likewise
    
    
    // Work that would normally be done here is shunted to Init() so that derived classes can override it.
    // Will never be called, as this function is private and this class cannot be instantiated.
    void Start()
    {
        Init();
    }

    // Initialize what we can without additional info from the creating script.
    // CALL THIS from derived classes' Init()!
    virtual void Init()
    {
        // size requires input from the store, handled in InitSize

        // movement speeds taken from Wikipedia on preferred walking speed, assuming no transitions to full running
        maxWalkSpeed = 1.4f;
        maxBoostSpeed = 2.5f;
        velocity = Vector2.zero;
        maxWalkAcceleration = 1.0f;
        maxBoostAcceleration = 2.0f;
    }

    // CALL THIS from the creating script!
    void InitSize(float _radius, float _distance)
    {
        radius = _radius;
        physicalDistance = _distance;

        // TODO: display size and "bubble"
        // TODO: add circle collider
    }

    // The X and Y components are taken only to indicate direction to move 
    // towards.  Z indicates "panic level" and controls the extent to which 
    // "boost mode" is used, where 0 is no boost and 1 is full boost.  Z  
    // values outside [0,1] will be clamped by the caller.
    abstract Vector3 GetMovementIntent();  // must return how the character intends to move on a local level - NOT eventual goal point for a task list item!

    // As with Start(), work that would normally be here is shunted to other functions so derived classes can override them.
    // Will never be called, as this function is private and this class cannot be instantiated.
    void Update()
    {
        doMovementUpdate();
    }

    virtual void doMovementUpdate()
    {
        // split movement intent into direction and panic level
        Vector3 movementIntent = GetMovementIntent();
        Vector2 movementDirection = new Vector2(movementIntent.x, movementIntent.y);
        float boostLevel = movementIntent.z;  // needs to be clamped
        if (boostLevel < 0) { boostLevel = 0.0f; }
        else if (boostLevel > 1) { boostLevel = 1.0f; }

        // calculate velocity we "want" to be moving at (given infinite acceleration)
        Vector2 targetVelocity = movementDirection.normalized * Mathf.Lerp(maxWalkSpeed, maxBoostSpeed, boostLevel);  // Lerp handles scaling between walk and boost for us

        // calculate acceleration towards that velocity
        Vector2 targetAccelerationDirection = targetVelocity - velocity;
        targetAccelerationDirection.Normalize();
        float frameAccelerationMagnitude = Mathf.Lerp(maxWalkAcceleration, maxBoostAcceleration, boostLevel) * Time.deltaTime;
        // MoveTowards doesn't come in Vector2 form, so accelerate each component individually (scaling by the magnitude of acceleration in that component)
        velocity.x = Mathf.MoveTowards(velocity.x, targetVelocity.x, Math.Abs(targetAccelerationDirection.x) * frameAccelerationMagnitude);
        velocity.y = Mathf.MoveTowards(velocity.y, targetVelocity.y, Math.Abs(targetAccelerationDirection.y) * frameAccelerationMagnitude);

        // movement for this frame
        transform.Translate(velocity * Time.deltaTime);

        // collision handling
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, radius);  // will always include ourselves
        foreach (Collider2D hit in hits)  // move back out of anything we're overlapping
        {
            if (hit == ownCollider) { continue; }  // trying to move out of ourselves would be silly

            ColliderDistance2D colliderDistance = hit.Distance(ownCollider);  // shortest displacement between two colliders along with handy properties

            if (colliderDistance.isOverlapped)  // still have to check this because prior collision resolutions might have pushed us out
            {
                transform.Translate(colliderDistance.pointA - colliderDistance.pointB);  // push us out by that minimum displacement
            }
        }
    }
}
