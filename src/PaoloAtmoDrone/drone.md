# Task: Space Engineers Programmable Block Script - Atmospheric Combat & Docking Drone (Bottom-Thrusters Only)

## Context
You are an expert C# developer specializing in Space Engineers In-Game Scripts (Programmable Block API). You need to create a new script project inside a standalone folder within the `src` directory of the solution. The script must be optimized, single-file (as per SE requirements), and strictly adhere to the game's execution limits (avoiding `InstructionOverflow`).

## Drone Grid Specifications
The script will control a minimal, lightweight atmospheric drone with the following specific hardware layout:
*   **Thrusters:** 2x Atmospheric Thrusters pointed **strictly DOWNWARDS** (No forward, backward, or sideways thrusters. Movement requires tilting the drone using gyroscopes, similar to a real-world quadcopter).
*   **Power:** 1x Small Reactor (connected to the conveyor network to pull Uranium Ingot) + 2x Small Batteries.
*   **Weapons:** 2x Fixed Gatling Guns pointed **FORWARD** (connected to the conveyor network).
*   **Utility/Control:** 
    *   1x Remote Control block (oriented forward).
    *   1x Sensor block (pointed forward).
    *   1x Camera block (pointed forward, aligned with weapons for raycasting if needed).
    *   1x Antenna.
    *   1x Gyroscope.
    *   1x Small Connector pointed **DOWNWARDS** (located in the center/belly of the drone).

## Behavior & Logic Requirements

### 1. Flight Dynamics (Bottom-Thrusters Only)
*   Since the drone *only* has downward thrusters, all horizontal translation (forward/backward/strafing) and altitude control must be handled by calculating the necessary pitch and roll via the **Gyroscope** to vector the thrust, combined with adjusting the thrust override/limit of the downward thrusters.
*   Implement a smooth PID (or simplified proportional) controller to maintain hover, translate, and orient towards targets.

### 2. Operational Modes
The drone operates in three primary states: **Orbit/Guard**, **Intercept/Attack**, and **Return to Base (RTB)**.

*   **Orbit/Guard Mode (Default):**
    *   The drone must loiter/hover in the air within a **50-meter radius of the player** (detect the player via the Remote Control's broadcast, GPS coordinates, or Sensor).
    *   Maintain a safe altitude above the terrain to prevent crashing.

*   **Intercept/Attack Mode:**
    *   Use the forward-facing **Sensor** (and/or Camera raycast) to detect hostile targets (enemies/pirates).
    *   When an enemy is detected, pivot to face the target directly with the forward Gatling guns and engage within effective range.
    *   Prioritize staying alive while maintaining line of sight to shoot.

*   **Return to Base (RTB) & Docking Mode:**
    *   **Trigger Conditions:** Trigger RTB immediately if:
        1. Batteries are critically low AND the Reactor has no Uranium Ingot left.
        2. OR Gatling guns run completely out of ammunition.
    *   **Navigation:** Navigate toward the base. The base position is dynamic and tracked via a specific **Base Beacon** (the script should look for a beacon with a configurable name or substring, e.g., "[Base]").
    *   **Docking:** The drone must precisely align and dock with a designated small connector at the base. 

### 3. Command System & Docking Setup (Argument Parsing)
The script must handle the following string argument passed via the Programmable Block:
*   `setdock`: The player will manually fly the drone exactly into the docking position (connected to the base connector). Running this command must save the *relative offset matrix* (position and orientation) of the docking port relative to the Base Beacon. This allows the drone to find the exact docking port even if the base moves.

### 4. Communication & Telemetry
*   The drone must continuously broadcast its current status via the **Antenna**.
*   Status update string should include: Current State (Guarding/Attacking/RTB/Docked), Battery %, Ammo Status, and Critical Warnings.

## Code Architecture & Constraints
*   Use `UpdateFrequency.Update1` or `UpdateFrequency.Update10` appropriately to balance performance and reaction time.
*   Implement robust block fetching with clear error reporting via `Echo` if essential blocks (Remote Control, Gyro, Thrusters) are missing or destroyed.
*   Keep the codebase clean, well-commented, and self-contained within the `Program` class structure required by Space Engineers.