using System;
using System.Collections.Generic;
using VRageMath;
using VRage.Game;
using VRage.Library;
using System.Text;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Ingame;
using Sandbox.Common;
using Sandbox.Game;
using VRage.Collections;
using VRage.Game.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;

namespace ScriptingClass
{
	public class Program : MyGridProgram
	{

		#region Introduction
		/*
        Introduction
        ----------------
        Hello and thank you for downloading Rdav's AI Mining Code
        Rdav's AI Mining Code is an easy-setup Code that allows a player
        to convert any vessel to a fully automated tunneling miner.
        
        Simply paste this code into a programmable block and in the terminal
        the code will help you set this code up and give instructions.
        
        All the code is designed to be pretty easy to navigate 
        and the code will help you set it up automatically
        any questions or queries don't hesitate to contact me!

        
        Rdav 08/04/18
           
        Suggestions Planned Features
        -----------------------------
        - Let me know what you think needs adding to code!

         ChangeLog:
         * Release Version
         * Updated: changed loc of line 56
         * Updated serialization (new grid error)
         * Updated isempty conditions //Update 002
         * Updated mining with face
         * updated not drop off in middle of row //Update 003
         * Updated full limit system
         * updated for RC exception fix
         * Updated performance issues with large roids (capped ct) //Update 004

        */
		#endregion

		//DO NOT TOUCH ANYTHING BELOW THIS LINE UNLESS INSTRUCTED =======================

		//Code Constructor For Initialisation
		public Program()
		{ FirstTimeSetup(); }

		//Code Saves For Further Functions
		public void Save()
		{ SaveCode(); }

		//Primary Code Runtime
		//========================

		//Setup Of Code Constants
		#region SETUP

		//STORED VARIABLES
		//---------------------------------------------------------------------------------------------------------------------- 

		//SUBCATEGORY PERMANENT ASSIGNMENTS: 
		string VERSION = "004"; //Script Version
		double PrecisionMaxAngularVel = 0.6; //Maximum Precision Ship Angular Velocity
		double RotationalSensitvity = 1; //Gain Applied To Gyros
		bool ThrustCountOverride = true; //Togglable Override On Thrust Count

		#endregion

		//Main Method Runtime
		#region Main Method Autominer
		void Main(string argument)
		{
			try
			{

				//Sets Up Code Runtime Indicators & Docking Route Checks
				//---------------------------------------------------------
				OP_BAR();
				Echo("General Information:\n----------------------------");

				//System Error Readouts And Diag
				//---------------------------------
				#region Block Error Readouts

				if (RC == null || RC.CubeGrid.GetCubeBlock(RC.Position) == null)
				{ Echo("No Remote Control Found,\nInstall Forward Facing Remote Control Block And Press Recompile"); RC = null; return; }
				if (CONNECTOR == null || CONNECTOR.CubeGrid.GetCubeBlock(CONNECTOR.Position) == null)
				{ Echo("No Connector Found,\nInstall Connector And Press Recompile "); CONNECTOR = null; return; }
				if (SENSOR == null || SENSOR.CubeGrid.GetCubeBlock(SENSOR.Position) == null)
				{ Echo("No Sensor Found,\nInstall Sensor For Asteroid Detection And Press Recompile,\n(all sensor settings will automatically be set)"); SENSOR = null; return; }
				if (GYRO == null || GYRO.CubeGrid.GetCubeBlock(GYRO.Position) == null)
				{ Echo("No Gyro Found,\nInstall Gyro And Press Recompile"); GYRO = null; return; }

				if (CAF2_THRUST.Count > 15 && ThrustCountOverride)
				{
					Echo("Large Amount Of Thrusters Detected\nProgram Terminated To Prevent Performance Issues\n" +
					  "Remove Unecessary Thrusters And Press Recompile (15 max)\n" +
					  "This safety measure can be disabled on line 56"); return;
				}
				#endregion

				//Dockpoint Handler
				//-----------------------
				Auto_DockpointDetect(); //Automatically Detects Docking Route
				var DOCKLIST = new List<Vector3D>();
				DOCKLIST.Add(DockPos3.Val);
				DOCKLIST.Add(DockPos2.Val);
				DOCKLIST.Add(DockPos1.Val);

				if (DOCKLIST[0] == new Vector3D()) //Returns Error If No Docking Route
				{ Echo("Cannot Find Docking Route\nPlease Dock To (Static) Connector\nTo Use As A Drop-Off Point"); return; }

				//Manual Resetter Returns To Base
				//--------------------------------
				if (argument == "FIN")
				{ MININGSTATUS.Val = "FIN"; HASFINISHED.Val = true; }
				if (argument == "RETURN")
				{ MININGSTATUS.Val = "FULL"; HASFINISHED.Val = true; }

				//Manual Override Enabler
				//-----------------------------
				bool ALL_RUN = true;
				foreach (var item in CONTROLLERS)
				{ if ((item as IMyShipController).IsUnderControl) { ALL_RUN = false; } }
				if (ALL_RUN == false)
				{
					//Sets Standard Delogging Procedures
					RC.SetAutoPilotEnabled(false);
					for (int j = 0; j < CAF2_THRUST.Count; j++)
					{ (CAF2_THRUST[j] as IMyThrust).ThrustOverride = 0; (CAF2_THRUST[j] as IMyThrust).Enabled = true; }
					GYRO.GyroOverride = false;
					CONNECTOR.Enabled = true;
					Echo("Manual Override Engaged\nStop controlling ship to continue program");

					//Sets Docked Status If Docked
					if (CONNECTOR.Status == MyShipConnectorStatus.Connected)
					{ COORD_ID.Val = 2; }
					return;
				}

				//Updates Coordinates (not size) in a new Locator Scenario
				//-------------------------------------------------------------
				try
				{
					if (ISNOTBURIED.Val)
					{
						//Splits The Code Into Chunks
						string[] InputData = Me.CustomData.Split('@');
						string[] InputGpsList = InputData[1].Split(':');
						Vector3D TryVector = new Vector3D(double.Parse(InputGpsList[2]), double.Parse(InputGpsList[3]), double.Parse(InputGpsList[4]));

						//Updates If Both Not Buried And Different
						if (TryVector != StoredAsteroidLoc.Val && ISNOTBURIED.Val)
						{
							StoredAsteroidLoc.Val = TryVector;
							MiningLogic(RC, DOCKLIST, GYRO, CONNECTOR, true);
							Echo("Updated Input Correctly");
							return;
						}
					}
				}
				catch { Echo("Incorrect Input Format,\n Refer Instructions, or Press 'Recompile' to reset."); }

				if (StoredAsteroidLoc.Val == new Vector3D()) //Returns Error If No Docking Route
				{ Echo("No Asteroid Input,\n Please Paste Valid GPS in Custom Data"); return; }

				//Updates A Sensor If Ship Has One
				//---------------------------------------------
				if (SENSOR != null && SENSOR.DetectAsteroids == false)
				{
					SENSOR.DetectAsteroids = true;
					SENSOR.DetectPlayers = false;
					SENSOR.DetectOwner = false;
					SENSOR.DetectLargeShips = false;
					SENSOR.DetectSmallShips = false;
					SENSOR.LeftExtend = 50;
					SENSOR.RightExtend = 50;
					SENSOR.TopExtend = 50;
					SENSOR.FrontExtend = 50;
					SENSOR.BottomExtend = 50;
					SENSOR.BackExtend = 50;
				}

				//Desets Thrust & Gyro
				//----------------------
				for (int j = 0; j < CAF2_THRUST.Count; j++)
				{ (CAF2_THRUST[j] as IMyThrust).ThrustOverride = 0; } //(CAF2_THRUST[j] as IMyThrust).Enabled = true; 

				//Runs Primary Mnining Logic (only if not 0,0,0)
				//-------------------------------------------------
				Echo("CurrentRoid: " + Vector3D.Round(StoredAsteroidLoc.Val));
				Echo("CurrentCentre: " + Vector3D.Round(StoredAsteroidCentre.Val));
				Echo("CurrentRoidSize: " + Math.Round(StoredAsteroidDiameter.Val) + " Metres");

				Echo("Is Vanilla RC A: " + Math.Round(StoredAsteroidDiameter.Val) + " Metres");
				Echo("Outside Asteroid? " + ISNOTBURIED.Val);
				Echo("Runtime: " + Math.Round(Runtime.LastRunTimeMs, 3) + " Ms");
				Echo("Version: " + VERSION + "\n");

				MiningLogic(RC, DOCKLIST, GYRO, CONNECTOR, false);
			}
			catch (Exception e)
			{ Echo(e + ""); }
		}
		#endregion

		//Function Specific Functions
		//============================

		//Used For Mining Management
		#region Mining Logic #RFC#
		/*=======================================================================================                             
          Function: Mining Logic                  
          ---------------------------------------                            
          function will: The next generation of Rc manager, will automatically compensate for
                         drifting to ensure the ship arrives on target quickly.
        //----------==--------=------------=-----------=---------------=------------=-----=-----*/
		Savable_Vector StoredAsteroidLoc = new Savable_Vector();
		Savable_Vector StoredAsteroidCentre = new Savable_Vector();
		Savable_Double StoredAsteroidDiameter = new Savable_Double();
		Savable_Bool AbleToMine = new Savable_Bool();
		void MiningLogic(IMyRemoteControl RC, List<Vector3D> DOCK_ROUTE, IMyGyro GYRO, IMyShipConnector CONNECTOR, bool RESET)
		{
			//Resets Function If A New Roid Is Detected
			if (RESET)
			{
				StoredAsteroidDiameter.Val = 0;
				StoredAsteroidCentre.Val = StoredAsteroidLoc.Val;
				ROW.Val = 1;
				COLUMN.Val = 1;
				MININGSTATUS.Val = "MINE";
				HASFINISHED.Val = false;
				return;
			}
			Echo("Mining Logic:\n--------------");

			//Sets If Full Or Not
			bool IsEmpty = (SHIP_DRILLS[0].GetInventory().CurrentMass < 100); //sets primary is empty   
			foreach (var CargoContainer in Cargo)
			{
				IMyInventory CurrentCargo = CargoContainer.GetInventory(0);
				IsEmpty = CurrentCargo.CurrentMass > 900 ? false : IsEmpty;
			}

			bool IsMassAboveThreshold = (double)SHIP_DRILLS[0].GetInventory().CurrentVolume > (double)SHIP_DRILLS[0].GetInventory().MaxVolume * 0.80;

			if (IsMassAboveThreshold) { Echo("Drill Inventory Is Currently Full"); }
			if (IsEmpty) { Echo("Drill Inventory Is Currently Empty"); }

			//bool IsEmpty = (CONNECTOR.GetInventory().CurrentMass < 100); //SHIP_DRILLS[0].GetInventory().MaxVolume - SHIP_DRILLS[0].GetInventory().CurrentVolume > 10 || 
			//bool IsMassAboveThreshold = CONNECTOR.GetInventory().CurrentMass > 200000; //use connector for time being
			if (IsEmpty && MININGSTATUS.Val != "FIN")
			{ MININGSTATUS.Val = "MINE"; }
			if (IsMassAboveThreshold && MININGSTATUS.Val != "FIN")
			{ MININGSTATUS.Val = "FULL"; HASFINISHED.Val = true; }

			//Sets Drills:
			if (SHIP_DRILLS[0].IsWorking == false && MININGSTATUS.Val == "MINE")
			{
				for (int j = 0; j < SHIP_DRILLS.Count; j++)
				{ (SHIP_DRILLS[j] as IMyShipDrill).Enabled = true; }
			}
			else if (SHIP_DRILLS[0].IsWorking == true && MININGSTATUS.Val != "MINE")
			{
				for (int j = 0; j < SHIP_DRILLS.Count; j++)
				{ (SHIP_DRILLS[j] as IMyShipDrill).Enabled = false; }
			}


			//If Full Go And Free Dock (stage 1)
			if (MININGSTATUS.Val == "FULL" && ISNOTBURIED.Val)
			{
				DockingIterator(true, DOCK_ROUTE, GYRO, CONNECTOR, RC);
				Echo("Status: Docking To Offload");
				return;
			}

			//If Empty And Docked And Want To Go Mine, Undock (stage 2)
			if (COORD_ID.Val != 0 && MININGSTATUS.Val == "MINE")
			{
				DockingIterator(false, DOCK_ROUTE, GYRO, CONNECTOR, RC);
				Echo("Status: Undocking");
				return;
			}

			//If Fin, then Stop operations (stage 4)
			if (MININGSTATUS.Val == "FIN" && ISNOTBURIED.Val)
			{
				DockingIterator(true, DOCK_ROUTE, GYRO, CONNECTOR, RC);
				Echo("Status: Finished Task, Returning To Drop Off");
				return;
			}

			//Always Calls Mine Function If Undocked And Not Full
			if (COORD_ID.Val == 0) //If Undocked
			{

				//Uses Sensor To Update Information On Asteroid Within 250m of Selected Asteroid (prevents Close Proximity Asteroid Failure)
				if (SENSOR.IsActive && (StoredAsteroidLoc.Val - RC.GetPosition()).Length() < 50 && StoredAsteroidCentre.Val == StoredAsteroidLoc.Val)
				{
					StoredAsteroidDiameter.Val = SENSOR.LastDetectedEntity.BoundingBox.Size.Length();
					StoredAsteroidCentre.Val = SENSOR.LastDetectedEntity.BoundingBox.Center;
				}

				//If No Asteroid Detected Goes To Location To Detect Asteroid
				if (StoredAsteroidDiameter.Val == 0)
				{
					RC_Manager(StoredAsteroidLoc.Val, RC, false);
					return; //No Need For Remainder Of Logic
				}

				//Toggles Should-be-Mining Based On Proximity
				double Dist_To_Mine_Start = (StoredAsteroidLoc.Val - RC.GetPosition()).Length();
				double Dist_To_Mine_Centre = (StoredAsteroidCentre.Val - RC.GetPosition()).Length();
				if (Dist_To_Mine_Start < 4) //Toggles Mining Mode On
				{ AbleToMine.Val = true; }
				if (Dist_To_Mine_Centre > StoredAsteroidDiameter.Val + 40) //Toggles Mining Mode Off
				{ AbleToMine.Val = false; }

				//Goes To Location And Mines
				if (AbleToMine.Val == false)
				{
					RC_Manager(StoredAsteroidLoc.Val, RC, false);
					ISNOTBURIED.Val = true;
				}
				else if (AbleToMine.Val == true)
				{
					double DistToDrill = Math.Sqrt(SHIP_DRILLS.Count) * 0.9; //Size of Ship
					if (Me.CubeGrid.ToString().Contains("Large")) //if large grid
					{ DistToDrill = DistToDrill * 1.5; }
					if (StoredAsteroidDiameter.Val > DistToDrill) //Only if drill size is bigger than ship 9prevents array handlign issues
					{ BoreMine(RC, StoredAsteroidLoc.Val, StoredAsteroidCentre.Val, StoredAsteroidDiameter.Val, DistToDrill, false); }
					else { Echo("No Asteroid Detected, Drill array too large"); }
				}

				Echo("Status: Mining");
				//Echo(SHIP_DRILLS[0].GetInventory().MaxVolume + " Inventory Count"); // - SHIP_DRILLS[0].GetInventory().CurrentVolume + 
			}

		}
		#endregion

		//Used For Generic BoreMining
		#region BoreMine #RFC#
		/*=======================================================================================                             
          Function: RC_MANAGER                     
          ---------------------------------------                            
          function will: The next generation of Rc manager, will automatically compensate for
                         drifting to ensure the ship arrives on target quickly.
        //----------==--------=------------=-----------=---------------=------------=-----=-----*/
		Savable_Int ROW = new Savable_Int();
		Savable_Int COLUMN = new Savable_Int();
		Savable_String MININGSTATUS = new Savable_String();
		Savable_Bool ISNOTBURIED = new Savable_Bool();
		Savable_Bool HASFINISHED = new Savable_Bool();
		List<IMyTerminalBlock> SHIP_DRILLS = new List<IMyTerminalBlock>();     //List Of all the ships drills
		void BoreMine(IMyRemoteControl RC, Vector3D ROID_START, Vector3D ROID_CENTRE, double ROID_DIAMETER, double SHIPSIZE, bool Reset)
		{

			//Setup Of Common Variables                         
			Vector3D DronePosition = RC.GetPosition();
			Vector3D Drone_To_Target = Vector3D.Normalize(ROID_CENTRE - DronePosition);

			//Generates XYZ Vectors
			Vector3D X_ADD = Vector3D.Normalize(ROID_CENTRE - ROID_START);//Characteristic 'Forward' vector 
			Vector3D Y_ADD = Vector3D.CalculatePerpendicularVector(X_ADD); //Characteristic 'Left' vector
			Vector3D Z_ADD = Vector3D.Cross(X_ADD, Y_ADD); //Characteristic 'Up' vector

			//Generates Array Of Starting Vectors
			int Steps = MathHelper.Clamp((int)((ROID_DIAMETER * 0.3) / SHIPSIZE), 1, 16); //How many horizontal passes of the ship are required to eat the roid
			double StepSize = SHIPSIZE;  //How big are those passes
			Vector3D[,] GridCoords = new Vector3D[Steps + 1, Steps + 1]; //i as ROW.Val, j as COLUMN.Val
			for (int i = 0; i < (Steps + 1); i++)
			{
				for (int j = 0; j < (Steps + 1); j++)
				{
					Vector3D Ipos = (Math.Pow(-1, i) == -1) ? ROID_START + StepSize * (i - 1) * -1 * Z_ADD : ROID_START + StepSize * i * Z_ADD;
					Vector3D Jpos = (Math.Pow(-1, j) == -1) ? Ipos + StepSize * (j - 1) * -1 * Y_ADD : Ipos + StepSize * j * Y_ADD;
					GridCoords[i, j] = Jpos;
				}
			}

			//Readouts
			Echo("Has Finished Tunnel: " + HASFINISHED.Val); Echo(StepSize + " 'Step' Size");
			Echo(ROW.Val + " /" + Steps + " Rows"); Echo(COLUMN.Val + " /" + Steps + " Columns"); //Echo((CurrentVectorEnd - DronePosition).Length() + " dist to iter++");

			//Generates Currently Targeted Vector As A Function Of 2 integers, ROW.Val and Depth 
			Vector3D CurrentVectorStart = GridCoords[ROW.Val, COLUMN.Val]; //Start Vector
			Vector3D CurrentVectorEnd = CurrentVectorStart + X_ADD * (((ROID_CENTRE - ROID_START).Length() - ROID_DIAMETER / 2) + ROID_DIAMETER * 0.8); //Accounts for small input

			//Sets IsBuried And Has Finished
			ISNOTBURIED.Val = (CurrentVectorStart - RC.GetPosition()).Length() < 4; //If Retracted Allows Switching Of Case
			if ((CurrentVectorEnd - DronePosition).Length() < 1) { HASFINISHED.Val = true; } //If Reached End Toggle Finished

			//Inputs To Autopilot Function
			double RollReqt = (float)(0.6 * (Vector3D.Dot(Z_ADD, RC.WorldMatrix.Down)));
			GyroTurn6(X_ADD * 999999999999999999, RotationalSensitvity, GYRO, RC, RollReqt, PrecisionMaxAngularVel);

			if (HASFINISHED.Val) //Reverses Once Finished
			{ Vector_Thrust_Manager(CurrentVectorEnd, CurrentVectorStart, RC.GetPosition(), 2, 0.5, RC); }
			else //else standard forward
			{ Vector_Thrust_Manager(CurrentVectorStart, CurrentVectorEnd, RC.GetPosition(), 1, 0.5, RC); }

			//Iterates Based On Proximity
			if ((CurrentVectorStart - DronePosition).Length() < 1 && ROW.Val == Steps && COLUMN.Val == Steps && HASFINISHED.Val)
			{
				MININGSTATUS.Val = "FIN";
				return;
			}
			if ((CurrentVectorStart - DronePosition).Length() < 1 && ROW.Val == Steps && HASFINISHED.Val)
			{ COLUMN.Val++; ROW.Val = 1; HASFINISHED.Val = false; }
			if ((CurrentVectorStart - DronePosition).Length() < 1 && HASFINISHED.Val)
			{ ROW.Val++; HASFINISHED.Val = false; }

		}
		#endregion

		//Used For Docking And Undocking Of Ships
		#region Docking Iterator #RFC#
		/*====================================================================================================================================                                  
        Secondary Function: Dock Iterator                          
        -----------------------------------------------------                                 
        Function will: Operate docking & undocking sequences for ships based on a Direct string Input                 
        //-=--------------=-----------=-----------=-------------------=-------------------=----------------------=----------------------------*/
		Savable_Int COORD_ID = new Savable_Int(); //Current Docking ID
		void DockingIterator(bool Docking, List<Vector3D> COORDINATES, IMyGyro GYRO, IMyShipConnector CONNECTOR, IMyRemoteControl RC)
		{

			//Logic Check To Check Coords Are Within Limits
			if (COORDINATES.Count < 3) { return; }

			//Changes Increment Based on Dock/Undock Requirement
			int TargetID = 0;
			int CurrentID = 0;
			int iter_er = 0;
			if (Docking == true)
			{ TargetID = 1; CurrentID = 0; iter_er = +1; }
			if (Docking == false)
			{ TargetID = 0; CurrentID = 1; iter_er = -1; }

			//Toggles State Of Thrusters Connectors And Gyros On The Ship
			if (Docking == true) { CONNECTOR.Connect(); }
			if (Docking == true && CONNECTOR.IsWorking == false) { CONNECTOR.Enabled = true; }
			if (Docking == false && CONNECTOR.IsWorking == true) { CONNECTOR.Disconnect(); CONNECTOR.Enabled = true; }
			if (CONNECTOR.Status == MyShipConnectorStatus.Connected && Docking == true)
			{
				//for (int j = 0; j < CAF2_THRUST.Count; j++)
				//{(CAF2_THRUST[j] as IMyThrust).Enabled = false; }
				GYRO.GyroOverride = false;
				return;
			}

			//Setting Up a Few Constants
			Vector3D RollOrienter = Vector3D.Normalize(COORDINATES[COORDINATES.Count - 1] - COORDINATES[COORDINATES.Count - 2]);
			Vector3D Connector_Direction = -1 * ReturnConnectorDirection(CONNECTOR, RC);
			double RollReqt = (float)(0.6 * (Vector3D.Dot(RollOrienter, Connector_Direction)));

			//Vertical Motion During Dock
			if (COORD_ID.Val == COORDINATES.Count - 1)
			{
				Vector3D DockingHeading = Vector3D.Normalize(COORDINATES[COORDINATES.Count - 3] - COORDINATES[COORDINATES.Count - 2]) * 9000000; //Heading
				GyroTurn6(DockingHeading, RotationalSensitvity, GYRO, RC, RollReqt, PrecisionMaxAngularVel); //Turn to heading
				if (Vector3D.Dot(RC.WorldMatrix.Forward, Vector3D.Normalize(DockingHeading)) > 0.98) //Error check for small rotational velocity
				{ Vector_Thrust_Manager(COORDINATES[COORD_ID.Val - TargetID], COORDINATES[COORD_ID.Val - CurrentID], CONNECTOR.GetPosition(), 5, 0.7, RC); }  //Thrusts to point
			}

			//Last/First External Coord During Dock
			else if (COORD_ID.Val == 0)
			{ RC_Manager(COORDINATES[0], RC, false); }  //Standard Auto for first location

			//Horizontal And Iterative Statement
			else
			{
				var HEADING = Vector3D.Normalize(COORDINATES[COORD_ID.Val - CurrentID] - COORDINATES[COORD_ID.Val - TargetID]) * 9000000;
				Vector_Thrust_Manager(COORDINATES[COORD_ID.Val - TargetID], COORDINATES[COORD_ID.Val - CurrentID], CONNECTOR.GetPosition(), 8, 1, RC); //Runs docking sequence 
				GyroTurn6(HEADING, RotationalSensitvity, GYRO, RC, RollReqt, PrecisionMaxAngularVel);
			}

			//Logic checks and iterates
			if (Docking == false && COORD_ID.Val == 0) { }
			else if ((CONNECTOR.GetPosition() - COORDINATES[COORD_ID.Val - CurrentID]).Length() < 1 || ((RC.GetPosition() - COORDINATES[COORD_ID.Val - CurrentID]).Length() < 10 && COORD_ID.Val == 0))
			{
				COORD_ID.Val = COORD_ID.Val + iter_er;
				if (COORD_ID.Val == COORDINATES.Count)
				{ COORD_ID.Val = COORDINATES.Count - 1; }
				if (COORD_ID.Val < 0)
				{ COORD_ID.Val = 0; }
			}
		}
		//----------==--------=------------=-----------=---------------=------------=-------==--------=------------=-----------=----------

		#endregion

		//Standardised First Time Setup
		#region First Time Setup #RFC#
		/*====================================================================================================================                             
        Function: FIRST_TIME_SETUP                   
        ---------------------------------------                            
        function will: Initiates Systems and initiasing Readouts to LCD
        Performance Cost:
       //======================================================================================================================*/
		//SUBCATEGORY STORED BLOCKS
		IMyRemoteControl RC;
		IMyShipConnector CONNECTOR;
		IMySensorBlock SENSOR;
		List<IMyLargeTurretBase> DIRECTORS = new List<IMyLargeTurretBase>();
		IMyRadioAntenna RADIO;
		IMyGyro GYRO;
		List<IMyTerminalBlock> CONTROLLERS = new List<IMyTerminalBlock>();
		List<IMyTerminalBlock> Cargo = new List<IMyTerminalBlock>();
		List<IMyTerminalBlock> DIRECTIONAL_FIRE = new List<IMyTerminalBlock>();  //Directional ship weaponry

		void FirstTimeSetup()
		{
			//Gathers Key Components
			//-----------------------------------
			//Sets Update Frequency
			Runtime.UpdateFrequency = UpdateFrequency.Update10;

			//Gathers Remote Control
			try
			{
				List<IMyTerminalBlock> TEMP_RC = new List<IMyTerminalBlock>();
				GridTerminalSystem.GetBlocksOfType<IMyRemoteControl>(TEMP_RC, b => b.CubeGrid == Me.CubeGrid);
				RC = TEMP_RC[0] as IMyRemoteControl;
				RC.ApplyAction("OnOff_On");
			}
			catch { }

			//GathersConnector  
			try
			{
				List<IMyTerminalBlock> TEMP_CON = new List<IMyTerminalBlock>();
				GridTerminalSystem.GetBlocksOfType<IMyShipConnector>(TEMP_CON, b => b.CubeGrid == Me.CubeGrid && b.CustomName.Contains("Ejector") == false);
				CONNECTOR = TEMP_CON[0] as IMyShipConnector;
				CONNECTOR.ApplyAction("OnOff_On");
			}
			catch { }

			//Sets Gyro
			try
			{
				List<IMyTerminalBlock> TEMP_GYRO = new List<IMyTerminalBlock>();
				GridTerminalSystem.GetBlocksOfType<IMyGyro>(TEMP_GYRO, b => b.CubeGrid == Me.CubeGrid);
				GYRO = TEMP_GYRO[0] as IMyGyro;
				GYRO.ApplyAction("OnOff_On");
			}
			catch { }

			//Sets Sensor
			try
			{
				List<IMyTerminalBlock> TEMP_SENSOR = new List<IMyTerminalBlock>();
				GridTerminalSystem.GetBlocksOfType<IMySensorBlock>(TEMP_SENSOR, b => b.CubeGrid == Me.CubeGrid);
				SENSOR = TEMP_SENSOR[0] as IMySensorBlock;
				SENSOR.ApplyAction("OnOff_On");
			}
			catch { }

			//Initialising Dedicated Cargo
			try
			{
				GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(Cargo, b => b.CubeGrid == Me.CubeGrid);
			}
			catch
			{ }

			//Gathers Antennae
			try
			{
				List<IMyTerminalBlock> TEMP = new List<IMyTerminalBlock>();
				GridTerminalSystem.GetBlocksOfType<IMyRadioAntenna>(TEMP, b => b.CubeGrid == Me.CubeGrid);
				RADIO = TEMP[0] as IMyRadioAntenna;
				RADIO.ApplyAction("OnOff_On");
				RADIO.SetValue<long>("PBList", Me.EntityId);
				RADIO.EnableBroadcasting = true;
				RADIO.Enabled = true;
			}
			catch { }

			//GathersControllers   
			try
			{
				GridTerminalSystem.GetBlocksOfType<IMyShipController>(CONTROLLERS, b => b.CubeGrid == Me.CubeGrid);
			}
			catch { }

			//Gathers Director Turret
			try
			{
				GridTerminalSystem.GetBlocksOfType<IMyLargeTurretBase>(DIRECTORS, b => b.CubeGrid == Me.CubeGrid);
			}
			catch { }

			//Gathers Drills
			try
			{
				GridTerminalSystem.GetBlocksOfType<IMyShipDrill>(SHIP_DRILLS, b => b.CubeGrid == Me.CubeGrid);
			}
			catch { }

			//Gathers Directional Weaponry
			try
			{
				GridTerminalSystem.GetBlocksOfType<IMyUserControllableGun>(DIRECTIONAL_FIRE,
					(block => block.GetType().Name == "MySmallMissileLauncher" || block.GetType().Name == "MySmallGatlingGun"
						|| block.GetType().Name == "MySmallMissileLauncherReload")); //Collects the directional weaponry (in a group)
			}
			catch { }

			//Runs Thruster Setup 
			try
			{
				CollectAndFire2(new Vector3D(), 0, 0, RC.GetPosition(), RC);
				for (int j = 0; j < CAF2_THRUST.Count; j++)
				{ CAF2_THRUST[j].SetValue<float>("Override", 0.0f); CAF2_THRUST[j].ApplyAction("OnOff_On"); }
			}
			catch { }

			//Loads Saved Data
			try
			{
				//Retrieves And Deserializes Saved Data
				//string DataInput = Me.CustomData;
				DeSerializer(Storage);
			}
			catch
			{ }

			//Creates User Interface
			try
			{
				Me.CustomData = "Paste Asteroid GPS Here: \n===========================\n@" + "GPS:Mine:" + StoredAsteroidLoc.Val.X + ":" + StoredAsteroidLoc.Val.Y + ":" + StoredAsteroidLoc.Val.Z +
					"@\n\nInstructions:\n===========================\nPaste GPS coords of point NEAR asteroid\nbetween the symbols." +
					"\nDo NOT include the 'at' symbol in the GPS name\nErrors Will be displayed in the terminal\n\n" +
					"Hints/Tips:\n===========================\n" +
					"-Look in the terminal for live mining progress\n" +
					"-Paste GPS near the ore you want collected for faster mining\n" +
					"-NEVER paste a GPS from inside an asteroid \n" +
					"-Rename any connectors you don't want the code\n to use as docking points as 'Ejector' \n" +
					"-The code uses the GPS as a starting point so for\n larger ships keep this GPS further away from the asteroid\n" +
					"-The miner will remember the orientation you dock with \n so remember to dock in the direction you want to launch\n" +
					"-You can reassign docking coordinates at any point\n by manually overriding and docking somewhere else \n" +
					"-To cancel a task and return miner to base \n run the PB with the argument 'FIN'";
			}
			catch
			{ }

		}

		void SaveCode()
		{
			//Serializes Savable Data
			//Me.CustomData = Serializer();
			Storage = Serializer();
		}
		#endregion

		//Used For Automatic Dockpoint Recognition
		#region Auto-DockpointDetect #RFC#
		/*=======================================================================================                             
          Function: Auto-DockpointDetect                  
          ---------------------------------------                            
          function will: Automatically detect dockpoints for usage
        //----------==--------=------------=-----------=---------------=------------=-----=-----*/
		Savable_Vector DockPos1 = new Savable_Vector(); //Conncetor Location
		Savable_Vector DockPos2 = new Savable_Vector(); //Straight Up Location
		Savable_Vector DockPos3 = new Savable_Vector(); //Straight Up And Forward Location
		IMyShipConnector OtherTempConnector;

		void Auto_DockpointDetect()
		{
			//if Docked Only And A new Dockpoint Assign New Dockpoint
			if (CONNECTOR.Status == MyShipConnectorStatus.Connected)
			{
				OtherTempConnector = CONNECTOR.OtherConnector;
				DockPos1.Val = OtherTempConnector.GetPosition() + OtherTempConnector.WorldMatrix.Forward * (0.75);
				DockPos2.Val = OtherTempConnector.GetPosition() + OtherTempConnector.WorldMatrix.Forward * (6);
				DockPos3.Val = DockPos2.Val + RC.WorldMatrix.Forward * 40;
				COORD_ID.Val = 2;
			}

			//If Other Connector Is not Null Reassign Docking Coordinates
			if (OtherTempConnector != null)
			{

			}

		}
		#endregion

		//Primary Generic Functions
		//==========================

		//Use For General Drone Flying:
		#region RC_Manager #RFC#
		/*=======================================================================================                             
          Function: RC_MANAGER                     
          ---------------------------------------                            
          function will: The next generation of Rc manager, will automatically compensate for
                         drifting to ensure the ship arrives on target quickly.
        //----------==--------=------------=-----------=---------------=------------=-----=-----*/
		void RC_Manager(Vector3D TARGET, IMyRemoteControl RC, bool TURN_ONLY)
		{
			//Uses Rotation Control To Handle Max Rotational Velocity
			//---------------------------------------------------------
			if (RC.GetShipVelocities().AngularVelocity.AbsMax() > PrecisionMaxAngularVel)
			{ Echo("Slowing Rotational Velocity"); RC.SetAutoPilotEnabled(false); return; }

			//Setup Of Common Variables                         
			//--------------------------------------------
			Vector3D DronePosition = RC.GetPosition();
			Vector3D Drone_To_Target = Vector3D.Normalize(TARGET - DronePosition);

			//Override Direction Detection
			//-------------------------------
			double To_Target_Angle = Vector3D.Dot(Vector3D.Normalize(RC.GetShipVelocities().LinearVelocity), Drone_To_Target);
			double Ship_Velocity = RC.GetShipVelocities().LinearVelocity.Length();

			//Turn Only: (Will drift ship automatically)
			//--------------------------------------------
			if (TURN_ONLY)
			{
				RC.ClearWaypoints();
				RC.AddWaypoint(TARGET, "1");
				RC.AddWaypoint(TARGET, "cc1");
				RC.ApplyAction("AutoPilot_On");
				RC.ApplyAction("CollisionAvoidance_Off");
				RC.ControlThrusters = false;
				return;
			}

			//Drift Cancellation Enabled:
			//-----------------------------
			if (To_Target_Angle < 0.4 && Ship_Velocity > 3)
			{
				Echo("Drift Cancellation Enabled");

				//Aim Gyro To Reflected Vector
				Vector3D DRIFT_VECTOR = Vector3D.Normalize(RC.GetShipVelocities().LinearVelocity);
				Vector3D REFLECTED_DRIFT_VECTOR = -1 * (Vector3D.Normalize(Vector3D.Reflect(DRIFT_VECTOR, Drone_To_Target)));
				Vector3D AIMPINGPOS = (-1 * DRIFT_VECTOR * 500) + DronePosition;

				//Sets Autopilot To Turn
				RC.ClearWaypoints();
				RC.AddWaypoint(AIMPINGPOS, "1");
				RC.AddWaypoint(AIMPINGPOS, "cc1");
				RC.ApplyAction("AutoPilot_On");
				RC.ApplyAction("CollisionAvoidance_Off");

			}

			//System Standard Operation:
			//---------------------------
			else
			{
				Echo("Drift Cancellation Disabled");

				//Assign To RC, Clear And Refresh Command                         
				RC.ClearWaypoints();
				RC.ControlThrusters = true;
				RC.AddWaypoint(TARGET, "1");
				RC.AddWaypoint(TARGET, "cc1");
				RC.ApplyAction("AutoPilot_On");                   //RC toggle 
				RC.ApplyAction("DockingMode_Off");                //Precision Mode
				RC.ApplyAction("CollisionAvoidance_On");          //Col avoidance

			}

		}
		#endregion

		//Use For Precise Turning (docking, mining, attacking)
		#region GyroTurn6 #RFC#
		/*=======================================================================================                             
          Function: GyroTurn6                    
          ---------------------------------------                            
          function will: The next generation of Gyroturn, designed to be performance optimised
                         over actuating performance, it detects orientation and directly applies overrides
        //----------==--------=------------=-----------=---------------=------------=-----=-----*/
		void GyroTurn6(Vector3D TARGET, double GAIN, IMyGyro GYRO, IMyRemoteControl REF_RC, double ROLLANGLE, double MAXANGULARVELOCITY)
		{
			//Ensures Autopilot Not Functional
			REF_RC.SetAutoPilotEnabled(false);
			Echo("Running Gyro Control Program");

			//Detect Forward, Up & Pos
			Vector3D ShipForward = REF_RC.WorldMatrix.Forward;
			Vector3D ShipUp = REF_RC.WorldMatrix.Up;
			Vector3D ShipPos = REF_RC.GetPosition();

			//Create And Use Inverse Quatinion                   
			Quaternion Quat_Two = Quaternion.CreateFromForwardUp(ShipForward, ShipUp);
			var InvQuat = Quaternion.Inverse(Quat_Two);
			Vector3D DirectionVector = Vector3D.Normalize(TARGET - ShipPos); //RealWorld Target Vector
			Vector3D RCReferenceFrameVector = Vector3D.Transform(DirectionVector, InvQuat); //Target Vector In Terms Of RC Block

			//Convert To Local Azimuth And Elevation
			double ShipForwardAzimuth = 0; double ShipForwardElevation = 0;
			Vector3D.GetAzimuthAndElevation(RCReferenceFrameVector, out ShipForwardAzimuth, out ShipForwardElevation);

			//Does Some Rotations To Provide For any Gyro-Orientation
			var RC_Matrix = REF_RC.WorldMatrix.GetOrientation();
			var Vector = Vector3.Transform((new Vector3D(ShipForwardElevation, ShipForwardAzimuth, ROLLANGLE)), RC_Matrix); //Converts To World
			var TRANS_VECT = Vector3.Transform(Vector, Matrix.Transpose(GYRO.WorldMatrix.GetOrientation()));  //Converts To Gyro Local

			//Applies To Scenario
			GYRO.Pitch = (float)MathHelper.Clamp((-TRANS_VECT.X * GAIN), -MAXANGULARVELOCITY, MAXANGULARVELOCITY);
			GYRO.Yaw = (float)MathHelper.Clamp(((-TRANS_VECT.Y) * GAIN), -MAXANGULARVELOCITY, MAXANGULARVELOCITY);
			GYRO.Roll = (float)MathHelper.Clamp(((-TRANS_VECT.Z) * GAIN), -MAXANGULARVELOCITY, MAXANGULARVELOCITY);
			GYRO.GyroOverride = true;

			//GYRO.SetValueFloat("Pitch", (float)((TRANS_VECT.X) * GAIN));     
			//GYRO.SetValueFloat("Yaw", (float)((-TRANS_VECT.Y) * GAIN));
			//GYRO.SetValueFloat("Roll", (float)((-TRANS_VECT.Z) * GAIN));
		}
		#endregion

		//Use For Precise Thrusting (docking, mining, attacking)
		#region CollectAndFire2 #RFC#
		/*=======================================================================================                             
          Function: COLLECT_AND_FIRE                      
          ---------------------------------------                            
          function will: Collect thrust pointing in a input direction and fire said thrust
                         towards that point, remember to deset
        //----------==--------=------------=-----------=---------------=------------=-----=-----*/
		class Thrust_info                   //Basic Information For Axial Thrust
		{
			public double PositiveMaxForce;
			public double NegativeMaxForce;
			public List<IMyThrust> PositiveThrusters;
			public List<IMyThrust> NegativeThrusters;
			public double VCF;
			public Thrust_info(Vector3D DIRECT, IMyGridTerminalSystem GTS, IMyCubeGrid MEGRID)
			{
				PositiveThrusters = new List<IMyThrust>(); NegativeThrusters = new List<IMyThrust>();
				List<IMyTerminalBlock> TEMP_RC = new List<IMyTerminalBlock>();
				GTS.GetBlocksOfType<IMyThrust>(PositiveThrusters, block => Vector3D.Dot(-1 * block.WorldMatrix.Forward, DIRECT) > 0.7 && block.CubeGrid == MEGRID);
				GTS.GetBlocksOfType<IMyThrust>(NegativeThrusters, block => Vector3D.Dot(block.WorldMatrix.Forward, DIRECT) > 0.7 && block.CubeGrid == MEGRID);
				double POWER_COUNT = 0;
				foreach (var item in PositiveThrusters)
				{ POWER_COUNT = POWER_COUNT + item.MaxEffectiveThrust; }
				PositiveMaxForce = POWER_COUNT;
				POWER_COUNT = 0;
				foreach (var item in NegativeThrusters)
				{ POWER_COUNT = POWER_COUNT + item.MaxEffectiveThrust; }
				NegativeMaxForce = POWER_COUNT;
			}
		}
		Thrust_info CAF2_FORWARD;
		Thrust_info CAF2_UP;
		Thrust_info CAF2_RIGHT;
		List<Thrust_info> CAFTHI = new List<Thrust_info>();

		List<IMyTerminalBlock> CAF2_THRUST = new List<IMyTerminalBlock>();
		bool C_A_F_HASRUN = false;
		double CAF2_BRAKING_COUNT = 99999999;

		double CAF_SHIP_DECELLERATION;                        //Outputs current decelleration
		double CAF_STOPPING_DIST;                             //Outputs current stopping distance
		double CAF_DIST_TO_TARGET;                            //Outputs distance to target

		void CollectAndFire2(Vector3D INPUT_POINT, double INPUT_VELOCITY, double INPUT_MAX_VELOCITY, Vector3D REFPOS, IMyRemoteControl RC)
		{
			//Function Initialisation
			//-------------------------------------------------------------------- 
			if (C_A_F_HASRUN == false)
			{
				//Initialise Classes And Basic System
				CAF2_FORWARD = new Thrust_info(RC.WorldMatrix.Forward, GridTerminalSystem, Me.CubeGrid);
				CAF2_UP = new Thrust_info(RC.WorldMatrix.Up, GridTerminalSystem, Me.CubeGrid);
				CAF2_RIGHT = new Thrust_info(RC.WorldMatrix.Right, GridTerminalSystem, Me.CubeGrid);
				CAFTHI = new List<Thrust_info>() { CAF2_FORWARD, CAF2_UP, CAF2_RIGHT };
				GridTerminalSystem.GetBlocksOfType<IMyThrust>(CAF2_THRUST, block => block.CubeGrid == Me.CubeGrid);
				C_A_F_HASRUN = true;

				//Initialises Braking Component
				foreach (var item in CAFTHI)
				{
					CAF2_BRAKING_COUNT = (item.PositiveMaxForce < CAF2_BRAKING_COUNT) ? item.PositiveMaxForce : CAF2_BRAKING_COUNT;
					CAF2_BRAKING_COUNT = (item.NegativeMaxForce < CAF2_BRAKING_COUNT) ? item.PositiveMaxForce : CAF2_BRAKING_COUNT;
				}
			}
			Echo("Running Thruster Control Program");

			//Generating Maths To Point and decelleration information etc.
			//-------------------------------------------------------------------- 
			double SHIPMASS = Convert.ToDouble(RC.CalculateShipMass().PhysicalMass);
			Vector3D INPUT_VECTOR = Vector3D.Normalize(INPUT_POINT - REFPOS);
			double VELOCITY = RC.GetShipSpeed();
			CAF_DIST_TO_TARGET = (REFPOS - INPUT_POINT).Length();
			CAF_SHIP_DECELLERATION = 0.75 * (CAF2_BRAKING_COUNT / SHIPMASS);
			CAF_STOPPING_DIST = (((VELOCITY * VELOCITY) - (INPUT_VELOCITY * INPUT_VELOCITY))) / (2 * CAF_SHIP_DECELLERATION);

			//If Within Stopping Distance Halts Programme
			//--------------------------------------------
			if (!(CAF_DIST_TO_TARGET > (CAF_STOPPING_DIST + 0.25)) || CAF_DIST_TO_TARGET < 0.25 || VELOCITY > INPUT_MAX_VELOCITY)
			{ foreach (var thruster in CAF2_THRUST) { (thruster as IMyThrust).ThrustOverride = 0; } return; }
			//dev notes, this is the most major source of discontinuity between theorised system response

			//Reflects Vector To Cancel Orbiting
			//------------------------------------
			Vector3D DRIFT_VECTOR = Vector3D.Normalize(RC.GetShipVelocities().LinearVelocity + RC.WorldMatrix.Forward * 0.00001);
			Vector3D R_DRIFT_VECTOR = -1 * Vector3D.Normalize(Vector3D.Reflect(DRIFT_VECTOR, INPUT_VECTOR));
			R_DRIFT_VECTOR = ((Vector3D.Dot(R_DRIFT_VECTOR, INPUT_VECTOR) < -0.3)) ? 0 * R_DRIFT_VECTOR : R_DRIFT_VECTOR;
			INPUT_VECTOR = Vector3D.Normalize((4 * R_DRIFT_VECTOR) + INPUT_VECTOR);

			//Components Of Input Vector In FUR Axis
			//----------------------------------------
			double F_COMP_IN = Vector_Projection(INPUT_VECTOR, RC.WorldMatrix.Forward);
			double U_COMP_IN = Vector_Projection(INPUT_VECTOR, RC.WorldMatrix.Up);
			double R_COMP_IN = Vector_Projection(INPUT_VECTOR, RC.WorldMatrix.Right);

			//Calculate MAX Allowable in Each Axis & Length
			//-----------------------------------------------
			double F_COMP_MAX = (F_COMP_IN > 0) ? CAF2_FORWARD.PositiveMaxForce : -1 * CAF2_FORWARD.NegativeMaxForce;
			double U_COMP_MAX = (U_COMP_IN > 0) ? CAF2_UP.PositiveMaxForce : -1 * CAF2_UP.NegativeMaxForce;
			double R_COMP_MAX = (R_COMP_IN > 0) ? CAF2_RIGHT.PositiveMaxForce : -1 * CAF2_RIGHT.NegativeMaxForce;
			double MAX_FORCE = Math.Sqrt(F_COMP_MAX * F_COMP_MAX + U_COMP_MAX * U_COMP_MAX + R_COMP_MAX * R_COMP_MAX);

			//Apply Length to Input Components and Calculates Smallest Multiplier
			//--------------------------------------------------------------------
			double F_COMP_PROJ = F_COMP_IN * MAX_FORCE;
			double U_COMP_PROJ = U_COMP_IN * MAX_FORCE;
			double R_COMP_PROJ = R_COMP_IN * MAX_FORCE;
			double MULTIPLIER = 1;
			MULTIPLIER = (F_COMP_MAX / F_COMP_PROJ < MULTIPLIER) ? F_COMP_MAX / F_COMP_PROJ : MULTIPLIER;
			MULTIPLIER = (U_COMP_MAX / U_COMP_PROJ < MULTIPLIER) ? U_COMP_MAX / U_COMP_PROJ : MULTIPLIER;
			MULTIPLIER = (R_COMP_MAX / R_COMP_PROJ < MULTIPLIER) ? R_COMP_MAX / R_COMP_PROJ : MULTIPLIER;

			//Calculate Multiplied Components
			//---------------------------------
			CAF2_FORWARD.VCF = ((F_COMP_PROJ * MULTIPLIER) / F_COMP_MAX) * Math.Sign(F_COMP_MAX);
			CAF2_UP.VCF = ((U_COMP_PROJ * MULTIPLIER) / U_COMP_MAX) * Math.Sign(U_COMP_MAX);
			CAF2_RIGHT.VCF = ((R_COMP_PROJ * MULTIPLIER) / R_COMP_MAX) * Math.Sign(R_COMP_MAX);

			//Runs System Thrust Application 
			//----------------------------------
			Dictionary<IMyThrust, float> THRUSTVALUES = new Dictionary<IMyThrust, float>();
			foreach (var thruster in CAF2_THRUST) { THRUSTVALUES.Add((thruster as IMyThrust), 0f); }

			foreach (var THRUSTSYSTM in CAFTHI)
			{
				List<IMyThrust> POSTHRUST = THRUSTSYSTM.PositiveThrusters;
				List<IMyThrust> NEGTHRUST = THRUSTSYSTM.NegativeThrusters;
				if (THRUSTSYSTM.VCF < 0) { POSTHRUST = THRUSTSYSTM.NegativeThrusters; NEGTHRUST = THRUSTSYSTM.PositiveThrusters; }
				foreach (var thruster in POSTHRUST) { THRUSTVALUES[thruster as IMyThrust] = (float)(Math.Abs(THRUSTSYSTM.VCF)) * (thruster as IMyThrust).MaxThrust; }
				foreach (var thruster in NEGTHRUST) { THRUSTVALUES[thruster as IMyThrust] = 1; }//(float)0.01001;}
				foreach (var thruster in THRUSTVALUES) { thruster.Key.ThrustOverride = thruster.Value; } //thruster.Key.ThrustOverride = thruster.Value;
			}
		}
		//----------==--------=------------=-----------=---------------=------------=-------==--------=-----
		#endregion

		//Used For Precise Thrusting Along A Vector (docking, mining, attacking)
		#region Vector Thrust Manager #RFC#
		/*====================================================================================================================================                                  
        Secondary Function: PRECISION MANAGER                            
        -----------------------------------------------------                                 
        Function will: Given two inputs manage vector-based thrusting               
        Inputs: DIRECTION, BLOCK                 
        //-=--------------=-----------=-----------=-------------------=-------------------=----------------------=----------------------------*/
		void Vector_Thrust_Manager(Vector3D PM_START, Vector3D PM_TARGET, Vector3D PM_REF, double PR_MAX_VELOCITY, double PREC, IMyRemoteControl RC)
		{
			Vector3D VECTOR = Vector3D.Normalize(PM_START - PM_TARGET);
			Vector3D GOTOPOINT = PM_TARGET + VECTOR * MathHelper.Clamp((((PM_REF - PM_TARGET).Length() - 0.2)), 0, (PM_START - PM_TARGET).Length());
			double DIST_TO_POINT = MathHelper.Clamp((GOTOPOINT - PM_REF).Length(), 0, (PM_START - PM_TARGET).Length());

			if (DIST_TO_POINT > PREC)
			{ CollectAndFire2(GOTOPOINT, 0, PR_MAX_VELOCITY * 2, PM_REF, RC); }
			else
			{ CollectAndFire2(PM_TARGET, 0, PR_MAX_VELOCITY, PM_REF, RC); }
		}
		//----------==--------=------------=-----------=---------------=------------=-------==--------=------------=-----------=----------
		#endregion

		//Use For Magnitudes Of Vectors In Directions
		#region Vector Projection #RFC#
		/*=================================================                           
          Function: Vector Projection Useful For Generic 
                    quantity in direction algorithms           
          ---------------------------------------     */
		double Vector_Projection(Vector3D IN, Vector3D Axis)
		{
			double OUT = 0;
			OUT = Vector3D.Dot(IN, Axis) / IN.Length();
			if (OUT + "" == "NaN")
			{ OUT = 0; }
			return OUT;
		}
		#endregion

		//Use For General Display
		#region RFC Function bar #RFC#
		/*=================================================                           
          Function: RFC Function bar #RFC#                  
          ---------------------------------------     */
		string[] FUNCTION_BAR = new string[] { "", " ===||===", " ==|==|==", " =|====|=", " |======|", "  ======" };
		int FUNCTION_TIMER = 0;                                     //For Runtime Indicator
		void OP_BAR()
		{
			FUNCTION_TIMER++;
			Echo("     ~ MKII RFC AI Running~  \n               " + FUNCTION_BAR[FUNCTION_TIMER] + "");
			if (FUNCTION_TIMER == 5) { FUNCTION_TIMER = 0; }
		}
		#endregion

		//Returns Connector Orientation As A String
		#region Connector Direction #RFC#
		/*=======================================================================================                             
          Function: Connector Direction                     
          ---------------------------------------                            
          function will: return a string for the RC to use for docking procedures                                                      
        //=======================================================================================*/
		Vector3D ReturnConnectorDirection(IMyShipConnector CONNECTOR, IMyRemoteControl RC)
		{
			if (CONNECTOR.Orientation.Forward == RC.Orientation.TransformDirection(Base6Directions.Direction.Down))
			{ return RC.WorldMatrix.Left; }  //Connector is the bottom of ship
			if (CONNECTOR.Orientation.Forward == RC.Orientation.TransformDirection(Base6Directions.Direction.Up))
			{ return RC.WorldMatrix.Right; }  //Connector is on the top of the ship
			if (CONNECTOR.Orientation.Forward == RC.Orientation.TransformDirection(Base6Directions.Direction.Right))
			{ return RC.WorldMatrix.Up; }  //Connector is on the left of the ship
			if (CONNECTOR.Orientation.Forward == RC.Orientation.TransformDirection(Base6Directions.Direction.Left))
			{ return RC.WorldMatrix.Down; }  //Connector is on the right of the ship
			return RC.WorldMatrix.Down;
		}
		#endregion

		//Generic Constructors Used For Serialization & Saving/Loading
		#region StandardSerializer #RFC#
		/*=======================================================================================                             
          Function: Serializer                  
          ---------------------------------------                            
          function will: Serialize & Deserialize any variable using the Savable_ tag
        //----------==--------=------------=-----------=---------------=------------=-----=-----*/

		//Serialization Lists (All static, bahhh)
		static List<Savable_String> SavableStrings = new List<Savable_String>();
		static List<Savable_Int> SavableInts = new List<Savable_Int>();
		static List<Savable_Vector> SavableVectors = new List<Savable_Vector>();
		static List<Savable_Double> SavableDoubles = new List<Savable_Double>();
		static List<Savable_Bool> SavableBools = new List<Savable_Bool>();

		//Serializable Variable Types
		class Savable_String //Savable String Interface
		{ public string Val = ""; public Savable_String() { SavableStrings.Add(this); } }
		class Savable_Int //Savable Int Interface
		{ public int Val = 0; public Savable_Int() { SavableInts.Add(this); } }
		class Savable_Vector //Savable Vector3D Interface
		{ public Vector3D Val = new Vector3D(0, 0, 0); public Savable_Vector() { SavableVectors.Add(this); } }
		class Savable_Double //Savable Double Interface
		{ public double Val = 0; public Savable_Double() { SavableDoubles.Add(this); } }
		class Savable_Bool//Savable Boolean Interface
		{ public bool Val = true; public Savable_Bool() { SavableBools.Add(this); } }

		//Methods For Serialization/Deserialization
		string Serializer()
		{

			//Saves GridId First (Prevents Reoccuring ID feature)
			string SaveString = Me.CubeGrid.EntityId + "";

			//Iterates Through Strings
			SaveString = SaveString + "/";
			foreach (var item in SavableStrings)
			{
				SaveString = SaveString + item.Val + "^";
			}

			//Iterates Through Ints
			SaveString = SaveString + "/";
			foreach (var item in SavableInts)
			{
				SaveString = SaveString + item.Val + "^";
			}

			//Iterates Through 3DVectors
			SaveString = SaveString + "/";
			foreach (var item in SavableVectors)
			{
				SaveString = SaveString + Vector3D.Round(item.Val, 2) + "^";
			}

			//Iterates Through Doubles
			SaveString = SaveString + "/";
			foreach (var item in SavableDoubles)
			{
				SaveString = SaveString + Math.Round(item.Val, 2) + "^";
			}

			//Iterates Through Bools
			SaveString = SaveString + "/";
			foreach (var item in SavableBools)
			{
				SaveString = SaveString + item.Val + "^";
			}

			//Clears Lists To Free Up Memory Allocation
			//(Cannot properly finalise)
			//SavableStrings.Clear();
			//SavableInts.Clear();
			//SavableVectors.Clear();
			//SavableDoubles.Clear();
			//SavableBools.Clear();
			return SaveString;
		}
		void DeSerializer(string Input)
		{
			//Splits Input Into Sections
			String[] SplitInput = Input.Split('/');
			Echo(Input);

			//Throw.Vals Error If Cannot Split
			if (SplitInput.Length < 5)
			{ Echo("Error During DeSerialization"); return; }

			//Exits If Grid ID is not equal to current
			if (SplitInput[0] != Me.CubeGrid.EntityId + "")
			{ Echo("New Grid, Serialization Cancelled"); return; }

			//Splits Strings And Assigns
			string[] SplitStrings = SplitInput[1].Split('^');
			for (int i = 0; i < SavableStrings.Count; i++)
			{
				SavableStrings[i].Val = SplitStrings[i];
			}

			//Splits Ints And Assigns
			string[] SplitInts = SplitInput[2].Split('^');
			for (int i = 0; i < SavableInts.Count; i++)
			{
				SavableInts[i].Val = int.Parse(SplitInts[i]);
			}

			//Splits 3DVectors And Assigns
			string[] SplitVectors = SplitInput[3].Split('^');
			for (int i = 0; i < SavableVectors.Count; i++)
			{
				Vector3D.TryParse(SplitVectors[i], out SavableVectors[i].Val);
			}

			//Splits Doubles And Assigns
			string[] SplitDoubles = SplitInput[4].Split('^');
			for (int i = 0; i < SavableDoubles.Count; i++)
			{
				SavableDoubles[i].Val = double.Parse(SplitDoubles[i]);
			}

			//Splits Bools And Assigns
			string[] SplitBools = SplitInput[5].Split('^');
            for (int i = 0; i < SavableBools.Count; i++)
			{
				SavableBools[i].Val = bool.Parse(SplitBools[i]);
			}

		}

		#endregion



	}
}
