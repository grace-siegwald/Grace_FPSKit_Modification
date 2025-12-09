using System;
using System.Collections.Generic;
using System.Numerics;
// using System.Threading.Tasks.Dataflow;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;
using Quaternion = UnityEngine.Quaternion;
#if UNITY_EDITOR
using UnityEditor;
#endif

[System.Serializable]
public class AmmoInventoryEntry
{
    [AmmoType]
    public int ammoType;
    public int amount = 0;
}

public class Controller : MonoBehaviour
{
    //Urg that's ugly, maybe find a better way
    public static Controller Instance { get; protected set; }

    public Camera MainCamera;
    public Camera WeaponCamera;
    
    public Transform CameraPosition;
    public Transform WeaponPosition;
    
    public Weapon[] startingWeapons;

    //this is only use at start, allow to grant ammo in the inspector. m_AmmoInventory is used during gameplay
    public AmmoInventoryEntry[] startingAmmo;

    [Header("Control Settings")]
    public float MouseSensitivity = 100.0f;
    public float PlayerSpeed = 5.0f;
    public float RunningSpeed = 7.0f;
    public float JumpSpeed = 5.0f;

    // -------- CHANGE 1: Added these new Variables -----------
    [Header("Physics Movement Settings")]
    [Tooltip("How much force to apply when moving on ground")]
    public float groundMoveForce = 50f;

    [Tooltip("How much force to apply when moving in air")]
    public float airMoveForce = 15f;

    [Tooltip("Maximum speed player can move on ground")]
    public float maxGroundSpeed = 8f;

    [Tooltip("Maximum speed player can move in air")]
    public float maxAirSpeed = 10f;

    [Tooltip("How quickly player stops on ground (higher = stops faster)")]
    public float groundDrag = 8f;

    [Tooltip("How quickly player slows in air (lower = more momentum)")]
    public float airDrag = 0.5f;

    [Header("Audio")]
    public RandomPlayer FootstepPlayer;
    public AudioClip JumpingAudioCLip;
    public AudioClip LandingAudioClip;
    
    float m_VerticalSpeed = 0.0f;
    bool m_IsPaused = false;
    int m_CurrentWeapon;
    
    float m_VerticalAngle, m_HorizontalAngle;
    public float Speed { get; private set; } = 0.0f;

    public bool LockControl { get; set; }
    public bool CanPause { get; set; } = true;

    public bool Grounded => m_Grounded;

    // CharacterController m_CharacterController;
    // Replaced Character Controller with RigidBody and CapsuleCollider components
    Rigidbody m_Rigidbody;
    CapsuleCollider m_CapsuleCollider;

    bool m_Grounded;
    float m_GroundedTimer;
    float m_SpeedAtJump = 0.0f;

    List<Weapon> m_Weapons = new List<Weapon>();
    Dictionary<int, int> m_AmmoInventory = new Dictionary<int, int>();

    void Awake()
    {
        Instance = this;
    }
    
    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        m_IsPaused = false;
        m_Grounded = true;
        
        MainCamera.transform.SetParent(CameraPosition, false);
        MainCamera.transform.localPosition = Vector3.zero;
        MainCamera.transform.localRotation = Quaternion.identity;
        // m_CharacterController = GetComponent<CharacterController>();
        // Replaced Character Controller finder
        m_Rigidbody = GetComponent<Rigidbody>();
        m_CapsuleCollider = GetComponent<CapsuleCollider>();


        for (int i = 0; i < startingWeapons.Length; ++i)
        {
            PickupWeapon(startingWeapons[i]);
        }

        for (int i = 0; i < startingAmmo.Length; ++i)
        {
            ChangeAmmo(startingAmmo[i].ammoType, startingAmmo[i].amount);
        }
        
        m_CurrentWeapon = -1;
        ChangeWeapon(0);

        for (int i = 0; i < startingAmmo.Length; ++i)
        {
            m_AmmoInventory[startingAmmo[i].ammoType] = startingAmmo[i].amount;
        }

        m_VerticalAngle = 0.0f;
        m_HorizontalAngle = transform.localEulerAngles.y;
    }

    void Update()
    {
        if (CanPause && Input.GetButtonDown("Menu"))
        {
            PauseMenu.Instance.Display();
        }

        FullscreenMap.Instance.gameObject.SetActive(Input.GetButton("Map"));

        // bool wasGrounded = m_Grounded;
        bool loosedGrounding = false;

        //Raycast downward to make sure we're on the ground
        float rayDistance = (m_CapsuleCollider.height / 2f) + 0.1f;
        Ray groundRay = new Ray(transform.position, Vector3.down);
        m_Grounded = Physics.Raycast(groundRay, rayDistance);

        //we define our own grounded and not use the Character controller one as the character controller can flicker
        //between grounded/not grounded on small step and the like. So we actually make the controller "not grounded" only
        //if the character controller reported not being grounded for at least .5 second;
        if (!m_Grounded)
        {
            if (m_Grounded)
            {
                m_GroundedTimer += Time.deltaTime;
                if (m_GroundedTimer >= 0.5f)
                {
                    loosedGrounding = true;
                    m_Grounded = false;
                }
            }
        }
        else
        {
            m_GroundedTimer = 0.0f;
            m_Grounded = true;
        }

        
        Speed = 0;
        Vector3 move = Vector3.zero;
        if (!m_IsPaused && !LockControl)
        {
            // Jump (we do it first as 
            if (m_Grounded && Input.GetButtonDown("Jump"))
            {
                Vector3 jumpVelocity = m_Rigidbody.linearVelocity;
                jumpVelocity.y = JumpSpeed;
                // m_Rigidbody.linearVelocity = jumpVelocity;
                
                // New jumping mechanism
                m_Rigidbody.AddForce(jumpVelocity, ForceMode.Impulse);

                m_Grounded = false;
                loosedGrounding = true;
                FootstepPlayer.PlayClip(JumpingAudioCLip, 0.8f,1.1f);
            }
            
            bool running = m_Weapons[m_CurrentWeapon].CurrentState == Weapon.WeaponState.Idle && Input.GetButton("Run");
            float actualSpeed = running ? RunningSpeed : PlayerSpeed;

            if (loosedGrounding)
            {
                m_SpeedAtJump = actualSpeed;
            }

            // Move around with WASD
            move = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
            if (move.sqrMagnitude > 1.0f)
                move.Normalize();

            float usedSpeed = m_Grounded ? actualSpeed : m_SpeedAtJump;

            // OLD CharacterController Movement
            // move = move * usedSpeed * Time.deltaTime;
            // move = transform.TransformDirection(move);
            // m_CharacterController.Move(move);

            // OLD RigidBody Physics Movement
            // move = transform.TransformDirection(move);
            // move = move * usedSpeed;

            // Vector3 targetVelocity = new Vector3(move.x, m_Rigidbody.linearVelocity.y, move.z);
            // m_Rigidbody.linearVelocity = Vector3.Lerp(m_Rigidbody.linearVelocity, targetVelocity, 0.1f);

            //-------------- CHANGE 2: New movement code --------------------------------------------------------
            // Convert input direction to "world space"
            Vector3 inputDirection = transform.TransformDirection(move);
            inputDirection.Normalize(); // Makes sure direction is 1 length ?

            // Choose force and max speed values based on whether were grounded or not...
            // condition ? valueiftrue : valueiffalse is a shorthand way to write an if-else statement
            float moveForce = m_Grounded ? groundMoveForce : airMoveForce; // use "stronger" value on ground, weaker value in air
            float maxSpeed = m_Grounded ? maxGroundSpeed : maxAirSpeed; 

            // Only apply force if we're actually pressing the movement key(s)
            if (inputDirection.magnitude > 0.1f)
            {
                // Calculate how fast we're currently moving
                Vector3 currentVelocity = new Vector3(m_Rigidbody.linearVelocity.x, 0, m_Rigidbody.linearVelocity.z);
                float currentSpeed = currentVelocity.magnitude;

                // Only apply force if we're below max speed
                if (currentSpeed < maxSpeed)
                {
                    // Apply force in the direction we're pressing
                    Vector3 forceToApply = inputDirection * moveForce;
                    // HERE is where was use ADDFORCE! We use ForceMode.Force for realistic acceleration
                    m_Rigidbody.AddForce(forceToApply, ForceMode.Force);
                
                    // Clamp to max speed if we exceed it!
                    if (currentSpeed >= maxSpeed)
                    {
                        Vector3 clampedVelocity = currentVelocity.normalized * maxSpeed;
                        m_Rigidbody.linearVelocity = new Vector3(clampedVelocity.x, m_Rigidbody.linearVelocity.y, clampedVelocity.z);
                    }
                }
            }
            // Apply appropriate drag (friction/air resistance)
            m_Rigidbody.linearDamping = m_Grounded ? groundDrag : airDrag;

            

            // Turn player
            float turnPlayer =  Input.GetAxis("Mouse X") * MouseSensitivity;
            m_HorizontalAngle = m_HorizontalAngle + turnPlayer;

            if (m_HorizontalAngle > 360) m_HorizontalAngle -= 360.0f;
            if (m_HorizontalAngle < 0) m_HorizontalAngle += 360.0f;
            
            Vector3 currentAngles = transform.localEulerAngles;
            currentAngles.y = m_HorizontalAngle;
            transform.localEulerAngles = currentAngles;

            // Camera look up/down
            var turnCam = -Input.GetAxis("Mouse Y");
            turnCam = turnCam * MouseSensitivity;
            m_VerticalAngle = Mathf.Clamp(turnCam + m_VerticalAngle, -89.0f, 89.0f);
            currentAngles = CameraPosition.transform.localEulerAngles;
            currentAngles.x = m_VerticalAngle;
            CameraPosition.transform.localEulerAngles = currentAngles;
  
            m_Weapons[m_CurrentWeapon].triggerDown = Input.GetMouseButton(0);

            // ------------------------- CHANGE 3: Replace Speed Calculation --------------------------------
            // old speed calculation:
            // Speed = move.magnitude / (PlayerSpeed * Time.deltaTime);

            // New: Calculate actual movement speed from RigidBody velocity
            Vector3 horizontalVelocity = new Vector3(m_Rigidbody.linearVelocity.x, 0, m_Rigidbody.linearVelocity.z);
            Speed = horizontalVelocity.magnitude;

            
            //---------------------- FIX for jittery gun animation ----------------------------------
            if (actualSpeed > 0.5f)
            {
                Speed = 0f;
            }
            else
            {
                Speed = actualSpeed;
            }


            if (Input.GetButton("Reload"))
                m_Weapons[m_CurrentWeapon].Reload();

            if (Input.GetAxis("Mouse ScrollWheel") < 0)
            {
                ChangeWeapon(m_CurrentWeapon - 1);
            }
            else if (Input.GetAxis("Mouse ScrollWheel") > 0)
            {
                ChangeWeapon(m_CurrentWeapon + 1);
            }
            
            //Key input to change weapon

            for (int i = 0; i < 10; ++i)
            {
                if (Input.GetKeyDown(KeyCode.Alpha0 + i))
                {
                    int num = 0;
                    if (i == 0)
                        num = 10;
                    else
                        num = i - 1;

                    if (num < m_Weapons.Count)
                    {
                        ChangeWeapon(num);
                    }
                }
            }
        }

        // Fall down / gravity
        // m_VerticalSpeed = m_VerticalSpeed - 10.0f * Time.deltaTime;
        // if (m_VerticalSpeed < -10.0f)
        //     m_VerticalSpeed = -10.0f; // max fall speed
        // var verticalMove = new Vector3(0, m_VerticalSpeed * Time.deltaTime, 0);
        // var flag = m_CharacterController.Move(verticalMove);
        // if ((flag & CollisionFlags.Below) != 0)
        //     m_VerticalSpeed = 0;

        if (m_Grounded)
        {
            FootstepPlayer.PlayClip(LandingAudioClip, 0.8f,1.1f);
        }
    }

    public void DisplayCursor(bool display)
    {
        m_IsPaused = display;
        Cursor.lockState = display ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = display;

        //------------------FIXED camera infinitely turning when paused-----------------------------
        if (display = true)
        {
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;
        }
    }

    void PickupWeapon(Weapon prefab)
    {
        //TODO : maybe find a better way than comparing name...
        if (m_Weapons.Exists(weapon => weapon.name == prefab.name))
        {//if we already have that weapon, grant a clip size of the ammo type instead
            ChangeAmmo(prefab.ammoType, prefab.clipSize);
        }
        else
        {
            var w = Instantiate(prefab, WeaponPosition, false);
            w.name = prefab.name;
            w.transform.localPosition = Vector3.zero;
            w.transform.localRotation = Quaternion.identity;
            w.gameObject.SetActive(false);
            
            w.PickedUp(this);
            
            m_Weapons.Add(w);
        }
    }

    void ChangeWeapon(int number)
    {
        if (m_CurrentWeapon != -1)
        {
            m_Weapons[m_CurrentWeapon].PutAway();
            m_Weapons[m_CurrentWeapon].gameObject.SetActive(false);
        }

        m_CurrentWeapon = number;

        if (m_CurrentWeapon < 0)
            m_CurrentWeapon = m_Weapons.Count - 1;
        else if (m_CurrentWeapon >= m_Weapons.Count)
            m_CurrentWeapon = 0;
        
        m_Weapons[m_CurrentWeapon].gameObject.SetActive(true);
        m_Weapons[m_CurrentWeapon].Selected();
    }

    public int GetAmmo(int ammoType)
    {
        int value = 0;
        m_AmmoInventory.TryGetValue(ammoType, out value);

        return value;
    }

    public void ChangeAmmo(int ammoType, int amount)
    {
        if (!m_AmmoInventory.ContainsKey(ammoType))
            m_AmmoInventory[ammoType] = 0;

        var previous = m_AmmoInventory[ammoType];
        m_AmmoInventory[ammoType] = Mathf.Clamp(m_AmmoInventory[ammoType] + amount, 0, 999);

        if (m_Weapons[m_CurrentWeapon].ammoType == ammoType)
        {
            if (previous == 0 && amount > 0)
            {//we just grabbed ammo for a weapon that add non left, so it's disabled right now. Reselect it.
                m_Weapons[m_CurrentWeapon].Selected();
            }
            
            WeaponInfoUI.Instance.UpdateAmmoAmount(GetAmmo(ammoType));
        }
    }

    public void PlayFootstep()
    {
        FootstepPlayer.PlayRandom();
    }
}
