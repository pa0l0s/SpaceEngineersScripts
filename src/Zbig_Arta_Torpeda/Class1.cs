
using System;
using System.Linq;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using VRageMath;
using VRage.Game;
using VRage.Collections;
using Sandbox.ModAPI.Ingame;
using VRage.Game.Components;
using VRage.Game.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Game.EntityComponents;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;

namespace Torpedy_Arta
{
	public sealed class Program : MyGridProgram
	{

		//=======================================================================
		//////////////////////////BEGIN//////////////////////////////////////////
		//=======================================================================       




		string COCKPIT = "Cockpit";
		string RADAR = "Radar";
		string TORPEDO = "Torpedo";
		string SOUND = "SoundLocked";
		string ARTA = "Cannons";
		static bool CENTER_SHOT = true;
		static float LOCK_POINT_DEPTH = 5;
		static string LCD = "#Ruler";
		static int LAUNCH_DELAY = 30;
		static float INTERCEPT_COURSE = 1.0f;
		static float MAX_VELOCITY = 180;
		static int WH_ARM_DIST = 100;
		static float TORPEDO_REFLECT_K = 4f;
		static float TORPEDO_GYRO_MULT = 1f;
		static float ACCEL_DET = 1.0f;
		static int WARHEAD_TIMER = 300;
		static float ROLL = 0.8f;
		static int WOLF_PACK_WELDING_TIME = 1200;
		static int WOLF_PACK_INTERVAL = 180;
		static int WOLF_PACK_COUNT = 4;

		static bool JAVELIN = false;
		static float JAVELIN_CURVE = 2.0f;

		static bool LASER_GUIDED = false;



		//---------


		static IMyGridTerminalSystem gts;
		static int Tick = 0;
		IMyCockpit cockpit;
		Radar radar;
		List<Torpedo> Torpedos;
		IMyTextPanel lcd;

		bool WolfPack = false;
		int WolfPackStart = 0;
		int WolfPackIndex = 0;
		List<int> WolfPackDelays;
		Artillery arta;
		IMySoundBlock sound;

		IMyCameraBlock SpotterCam;

		bool switchConnector = false;

		Program()
		{
			gts = GridTerminalSystem;

			cockpit = gts.GetBlockWithName(COCKPIT) as IMyCockpit;
			sound = gts.GetBlockGroupWithName(SOUND) as IMySoundBlock;
			radar = new Radar(RADAR);
			arta = new Artillery(ARTA, cockpit, 1200, 900);
			Torpedos = new List<Torpedo>();
			InitializeTorpedos();
			WolfPackDelays = new List<int>();
			lcd = gts.GetBlockWithName(LCD) as IMyTextPanel;
			if (lcd == null)
			{
				lcd = cockpit.GetSurface(0) as IMyTextPanel;
			}
		}

		void InitializeTorpedos()
		{
			Echo("Initializing torpedos: \n");
			int c = 0;
			for (int x = 1; x <= 8; x++)
			{
				string status = "";
				if (Torpedos.FindAll((b) => ((b.status == 1) && (b.Name == TORPEDO + x))).Count == 0)
					if (Torpedo.CheckBlocks(TORPEDO + x, out status))
					{
						Torpedos.Add(new Torpedo(TORPEDO + x,this));
						c++;
						Echo(status);
					}
			}
			Echo("\n" + c + " new torpedos initialized");
			Echo("\n" + Torpedos.FindAll((b) => ((b.status == 1))).Count + " torpedos ready for launch");
			Echo("\n" + Torpedos.FindAll((b) => ((b.status == 2))).Count + " torpedos on the way");
			Echo("\n" + Torpedos.Count + "torpedos in list");
		}

		void ClearAllTorpedos()
		{
			Torpedos.Clear();
		}

		void CleanGarbage()
		{
			List<int> killList = new List<int>();
			Echo("Cleaning: ");

			foreach (Torpedo t in Torpedos)
			{
				if (!t.CheckIntegrity())
				{
					killList.Add(Torpedos.IndexOf(t));
				}
			}
			Torpedos.RemoveIndices(killList);
			Echo("" + killList.Count + " torpedos trashed\n");
		}


		void Main(string arg, UpdateType uType)
		{
			if (uType == UpdateType.Update1)
			{
				if(switchConnector)
				{
					foreach (var torpedo in Torpedos)
					{
						torpedo.SwitchConnectorLock();
					}

					switchConnector = false;
				}

				Tick++;
				radar.Update();
				lcd.WriteText("LOCKED: " + radar.Locked, false);
				lcd.WriteText("\nTarget: " + radar.CurrentTarget.Name + ", tick: " + radar.LastLockTick, true);
				lcd.WriteText("\nDistance: " + Math.Round(radar.TargetDistance), true);
				lcd.WriteText("\nVelocity: " + Math.Round(radar.CurrentTarget.Velocity.Length()), true);
				if (radar.Locked)
					arta.AimCannons(radar.correctedTargetLocation, radar.CurrentTarget.Velocity);
				foreach (Torpedo t in Torpedos)
				{
					if (t.status == 2)
					{
						t.Update(radar.CurrentTarget, CENTER_SHOT ? radar.CurrentTarget.Position : radar.T, cockpit.GetPosition(), cockpit.WorldMatrix.Forward);
					}
				}
				if (WolfPack)
				{
					if ((Tick - WolfPackStart + 1) % WOLF_PACK_WELDING_TIME == 0)
					{
						CleanGarbage();
						InitializeTorpedos();
					}
					if ((radar.Locked) && ((Tick - WolfPackStart - 1) % WOLF_PACK_WELDING_TIME == 0))
					{
						foreach (Torpedo t in Torpedos)
						{
							Echo("\nTry Launch: ");
							Echo("\nWPI: " + WolfPackIndex);
							if (t.status == 1)
							{
								WolfPackIndex--;
								t.Launch(WolfPackDelays[WolfPackIndex]);
								break;
							}
						}
						if (WolfPackIndex <= 0)
							WolfPack = false;
					}
				}
				Echo("Runtime: " + Runtime.LastRunTimeMs);
			}
			else
			{
				switch (arg)
				{
					case "Lock":
						radar.Lock(true, 5000);
						if (radar.Locked)
						{

							arta.AimPoint = radar.CurrentTarget.Position;
							//sound.Play();
							//sound.Play();
							LASER_GUIDED = false;
							Runtime.UpdateFrequency = UpdateFrequency.Update1;
						}
						else
						{
							lcd.WriteText("NO TARGET", false);
							Runtime.UpdateFrequency = UpdateFrequency.None;
						}

						break;
					case "Laser":
						LASER_GUIDED = true;
						Runtime.UpdateFrequency = UpdateFrequency.Update1;
						break;
					case "CenterShot":
						CENTER_SHOT = !CENTER_SHOT;
						break;
					case "Init":
						CleanGarbage();
						InitializeTorpedos();
						break;
					case "SpotterCam":
						SpotterCam = gts.GetBlockWithName("SpotterCam") as IMyCameraBlock;
						if (SpotterCam != null)
						{
							SpotterCam.EnableRaycast = true;
							Echo("SpotterCam detected");
						}
						break;
					case "Spot":
						if (SpotterCam != null)
						{
							Echo("Spotting");
							MyDetectedEntityInfo spotterInfo = SpotterCam.Raycast(5000, 0, 0);
							if (!spotterInfo.IsEmpty())
							{
								Echo("Target Spotted");
								radar.correctedTargetLocation = spotterInfo.Position;
								radar.Lock();
								if (radar.Locked)
								{
									Echo("Target Locked");
									Runtime.UpdateFrequency = UpdateFrequency.Update1;
									/* sound.Play();
                                       sound.Stop();
                                       sound.Play();
                                       sound.Stop(); */
									LASER_GUIDED = false;
								}
							}
						}
						break;
					case "Stop":
						radar.StopLock();
						Runtime.UpdateFrequency = UpdateFrequency.None;
						break;
					case "Launch":
						if (radar.Locked)
							foreach (Torpedo t in Torpedos)
							{
								Echo("\nTry Launch: ");
								if (t.status == 1)
								{
									Echo("1 go");
									t.Launch();
									break;
								}
							}
						else
							Echo("No Target Lock");
						break;
					case "Test":
						Echo("\n Test:" + VerticalDelay(5000.0f, 1700.0f, 300));
						break;
					case "CCT":
						Runtime.UpdateFrequency = UpdateFrequency.Update1;
						arta.tensor_calculation = true;
						arta.tensor_calc_step = 0;
						break;
					case "Pack":
						if (radar.Locked)
						{
							WolfPackDelays.Clear();
							WolfPackDelays.Add(LAUNCH_DELAY);
							for (int x = 0; x < WOLF_PACK_COUNT - 1; x++)
							{
								WolfPackDelays.Add(VerticalDelay((float)radar.TargetDistance, (float)(WOLF_PACK_WELDING_TIME - WOLF_PACK_INTERVAL) * 1.666667f, WolfPackDelays[WolfPackDelays.Count - 1]));
							}
							WolfPack = true;
							WolfPackStart = Tick;
							WolfPackIndex = WOLF_PACK_COUNT;
						}
						break;
					default:
						break;
				}

			}
		}


		public class Artillery
		{
			List<IMyLargeMissileTurret> turrets = new List<IMyLargeMissileTurret>();
			double max_shot_vel;
			double shot_accel;
			public int tensor_calc_step = 0;
			public bool tensor_calculation = false;
			public Vector3D AimPoint = new Vector3D();
			IMyShipController shipcontroller;
			//тензор артиллерийских поправок
			//1 - расстояние до точки перехвата с шагом 500: 0=500, 1=1000...
			//2 - ортогональная скорость нашего корабля (по отношению к направлению выстрела)
			//с шагом 20: 0=-180, 1=-160...
			//3 - тангенциальная скорость нашего корабля (по отношению к направлению выстрела)
			//с шагом 10: 0=0, 1=10...
			//в подмассиве хранится в ячейке 0 - средняя скорость снаряда в ячейке 1 - тангенциальная поправка.

			double[,,,] CorrectionTensor = new double[35, 20, 20, 2];

			public Artillery(string groupname, IMyShipController controller, double shell_velocity = 1200, double shell_acceleration = 900)
			{
				IMyBlockGroup cannonsgroup = gts.GetBlockGroupWithName(groupname);
				cannonsgroup.GetBlocksOfType<IMyLargeMissileTurret>(turrets);
				shipcontroller = controller;
				max_shot_vel = shell_velocity;
				shot_accel = shell_acceleration;
			}

			public void CalculateCorrectionTensor()
			{
				double tang_vel = 0;
				double orth_vel = 0;
				double travel_distance = 0;
				double tang_correction = 0;
				double average_shell_velocity = 0;
				double shot_vel = 0;
				int travel_dist_index = 0;
				int prev_travel_dist_index = 0;
				int timeticks = 0;

				int tang_vel_index = tensor_calc_step / 19;
				int orth_vel_index = tensor_calc_step % 19;
				tang_vel = 0 + 10 * tang_vel_index;
				orth_vel = -180 + 20 * orth_vel_index + max_shot_vel;
				travel_distance = 0;
				tang_correction = 0;
				travel_dist_index = 0;
				prev_travel_dist_index = 0;
				timeticks = 0;
				average_shell_velocity = 0;
				while (travel_distance <= 17000)
				{
					timeticks++;
					orth_vel += shot_accel;
					shot_vel = Math.Sqrt(orth_vel * orth_vel + tang_vel * tang_vel);
					if (shot_vel > max_shot_vel)
					{
						orth_vel *= max_shot_vel / shot_vel;
						tang_vel *= max_shot_vel / shot_vel;
					}
					travel_distance += orth_vel / 60;
					tang_correction += tang_vel / 60;
					average_shell_velocity = (average_shell_velocity * (timeticks - 1) + orth_vel) / timeticks;
					travel_dist_index = (int)(travel_distance / 500);
					if (travel_dist_index > prev_travel_dist_index)
					{
						CorrectionTensor[travel_dist_index, orth_vel_index, tang_vel_index, 0] = average_shell_velocity;
						CorrectionTensor[travel_dist_index, orth_vel_index, tang_vel_index, 1] = tang_correction;
						prev_travel_dist_index = travel_dist_index;
					}

				}
				tensor_calc_step++;
				if (tensor_calc_step > 361)
					tensor_calculation = false;
			}

			private static Vector3D FindInterceptVector(Vector3D shotOrigin, double shotSpeed,
			Vector3D targetOrigin, Vector3D targetVel)
			{
				Vector3D dirToTarget = targetOrigin - shotOrigin;
				Vector3D dirToTargetNorm = Vector3D.Normalize(dirToTarget);
				Vector3D targetVelOrth = Vector3D.Dot(targetVel, dirToTargetNorm) * dirToTargetNorm;
				Vector3D targetVelTang = targetVel - targetVelOrth;
				Vector3D shotVelTang = targetVelTang;
				double shotVelSpeed = shotVelTang.Length();

				if (shotVelSpeed > shotSpeed)
				{
					return Vector3D.Normalize(targetVel) * shotSpeed;
				}
				else
				{
					double shotSpeedOrth = Math.Sqrt(shotSpeed * shotSpeed - shotVelSpeed * shotVelSpeed);
					Vector3D shotVelOrth = dirToTargetNorm * shotSpeedOrth;
					double t = dirToTarget.Length() / (shotSpeedOrth - targetVelOrth.Length());
					return (shotVelOrth + shotVelTang) * t;
				}
			}

			public Vector2D GetCorrection(double dist, double orth, double tang)
			{
				int dist_ind = (int)Math.Max(Math.Min(dist / 500, 34), 0);
				int orth_ind = (int)Math.Max(Math.Min((orth + 180) / 20, 19), 0);
				int tang_ind = (int)Math.Max(Math.Min(tang / 10, 19), 0);

				double vel = CorrectionTensor[dist_ind, orth_ind, tang_ind, 0];
				double tng = CorrectionTensor[dist_ind, orth_ind, tang_ind, 1];

				return new Vector2D(vel, tng);
				//return new Vector2D(1100,26);
			}

			public Vector2D GetCorrection2(double dist, double orth, double tang)
			{
				if (tang < 1.0)
					return new Vector2D(1200, 0);
				double c2 = max_shot_vel * max_shot_vel;
				double t0 = Math.Max((Math.Sqrt(max_shot_vel * max_shot_vel - tang * tang) - (max_shot_vel + orth)) / shot_accel, 0);
				double Vx0 = tang;
				double Vy0 = max_shot_vel + orth + shot_accel * t0;
				double X0 = tang * t0;
				double Y0 = (max_shot_vel + orth + shot_accel * t0 * 0.5) * t0;
				double Y = dist - Y0;
				double ex = Math.Exp(-shot_accel * Y / c2);
				double n1 = ex * Vx0 / max_shot_vel;
				double beta = Math.Abs(Math.Asin(Vx0 / max_shot_vel));
				double X1 = (beta - Math.Asin(n1)) * c2 / shot_accel;

				double n2 = Math.Sqrt(1 - n1 * n1 / c2) + 1;
				double ln = Math.Log(n1 / (n2 * Math.Tan(beta / 2)));
				double t = t0 - max_shot_vel * ln / shot_accel;

				return new Vector2D(dist / t, X0 + X1);
			}
			public void AimCannons(Vector3D TargetLocation, Vector3D TargetVelocity)
			{

				Vector3D MyTargetDir = AimPoint - turrets[0].GetPosition();
				double MyTargetDist = MyTargetDir.Length();
				MyTargetDir = Vector3D.Normalize(MyTargetDir);
				double MyOrthVelocity = shipcontroller.GetShipVelocities().LinearVelocity.Dot(MyTargetDir);
				Vector3D MyTangVelocity = Vector3D.Reject(shipcontroller.GetShipVelocities().LinearVelocity, MyTargetDir);

				Vector2D corrections = GetCorrection2(MyTargetDist, MyOrthVelocity, MyTangVelocity.Length());
				(gts.GetBlockWithName("Cockpit") as IMyCockpit).GetSurface(0).WriteText("v: " + corrections[0] + "  x: " + corrections[1] + "\n", false);

				Vector3D AimDir = FindInterceptVector(
					turrets[0].GetPosition(),
					corrections[0],
					TargetLocation,
					TargetVelocity);
				//(gts.GetBlockWithName(LCD) as IMyTextPanel).WriteText(""+AimDir.Length(),true);
				if ((corrections[1] > 0) && (MyTangVelocity.LengthSquared() > 1))
					AimDir -= (Vector3D.Normalize(MyTangVelocity) * corrections[1]);
				AimPoint = turrets[0].GetPosition() + AimDir;
				foreach (IMyLargeMissileTurret turret in turrets)
				{
					turret.SetTarget(AimPoint);
					/* if (radar.CurrentTarget.Velocity.Length() > 80)
                            turret.ApplyAction("ShootOnce");*/
				}
			}
		}

		public class Torpedo
		{
			List<IMyThrust> thrusters;
			List<IMyGyro> gyros;
			List<IMyWarhead> warheads;
			List<IMyBatteryBlock> batteries;
			List<IMyDecoy> decoys;
			List<IMyGasTank> hydrogenTanks;
			IMyRemoteControl remcon;
			IMyShipConnector connector;
			IMyShipMergeBlock merge;     


			int counter = 0;
			public int status = 0;
			public double MyVelocity = 0;
			private float ReflectK = TORPEDO_REFLECT_K;
			private float GyroMult = TORPEDO_GYRO_MULT;
			public string Name;
			int VerticalDelay = 300;
			bool Launched = false;
			// int CowntDown=5;
			// bool StartCountDown =false;
			Program _program;

			public Torpedo(string GroupName, Program program)
			{
				_program = program;
				Name = GroupName;
				List<IMyTerminalBlock> templist = new List<IMyTerminalBlock>();
				templist.Clear();

				_program.Echo("Init Connector");
				gts.GetBlocksOfType<IMyShipConnector>(templist, (b) => b.CustomName.Contains(GroupName));
				connector = templist[0] as IMyShipConnector;
				connector.ApplyAction(Actions.TURN_ON);
				if (connector.Status == MyShipConnectorStatus.Connectable)
					connector.ToggleConnect();
				templist.Clear();

				_program.Echo("Init Merge Block");
				gts.GetBlocksOfType<IMyShipMergeBlock>(templist, (b) => b.CustomName.Contains(GroupName));
				merge = templist[0] as IMyShipMergeBlock;
				merge.ApplyAction(Actions.TURN_ON);
				merge.Enabled = true;
				templist.Clear();

				_program.Echo("Init Remote Control");
				gts.GetBlocksOfType<IMyRemoteControl>(templist, (b) => b.CustomName.Contains(GroupName));
				remcon = templist[0] as IMyRemoteControl;
				//remcon.ApplyAction(Actions.TURN_ON);


				_program.Echo("Init Battery");
				batteries = new List<IMyBatteryBlock>();
				gts.GetBlocksOfType<IMyBatteryBlock>(batteries, (b) => b.CustomName.Contains(GroupName));
				foreach (IMyBatteryBlock battery in batteries)
				{
					battery.ApplyAction(Actions.TURN_ON);
					battery.Enabled = true;
				}

				_program.Echo("Init HYDROGEN tANKS");
				hydrogenTanks = new List<IMyGasTank>();
				gts.GetBlocksOfType<IMyGasTank>(hydrogenTanks, (b) => b.CustomName.Contains(GroupName));
				foreach (IMyGasTank hydrogenTank in hydrogenTanks)
				{
					hydrogenTank.ApplyAction(Actions.TURN_ON);
					hydrogenTank.Enabled = true;
					hydrogenTank.Stockpile = true; //Tankowanie zbiorników
				}

				_program.Echo("Init thrusters");
				thrusters = new List<IMyThrust>();
				gts.GetBlocksOfType<IMyThrust>(thrusters, (b) => b.CustomName.Contains(GroupName));
				_program.Echo("Init gyros");
				gyros = new List<IMyGyro>();
				gts.GetBlocksOfType<IMyGyro>(gyros, (b) => b.CustomName.Contains(GroupName));
				_program.Echo("Init warheads");
				warheads = new List<IMyWarhead>();
				gts.GetBlocksOfType<IMyWarhead>(warheads, (b) => b.CustomName.Contains(GroupName));
				_program.Echo("Init decoys");
				decoys = new List<IMyDecoy>();
				gts.GetBlocksOfType<IMyDecoy>(decoys, (b) => b.CustomName.Contains(GroupName));
				status = 1;
			}

			static public bool CheckBlocks(string GroupName, out string strBlockCheck)
			{
				strBlockCheck = GroupName;
				List<IMyTerminalBlock> templist = new List<IMyTerminalBlock>();

				//---------- CONNECTOR ------------ 
				templist.Clear();
				gts.GetBlocksOfType<IMyShipConnector>(templist, (b) => b.CustomName.Contains(GroupName));
				strBlockCheck += "\nConnectors: " + templist.Count;
				if (templist.Count == 0)
					return false;

				//---------- MERGE ------------ 
				templist.Clear();
				gts.GetBlocksOfType<IMyShipMergeBlock>(templist, (b) => b.CustomName.Contains(GroupName));
				strBlockCheck += "\nMerge: " + templist.Count;
				if (templist.Count == 0)
					return false;

				templist.Clear();
				gts.GetBlocksOfType<IMyShipConnector>(templist, (b) => (b.CustomName.Contains(GroupName) && (b as IMyShipConnector).Status > 0));
				strBlockCheck += "   Ready to connect: " + templist.Count;

				//---------- REM CON ------------
				templist.Clear();
				gts.GetBlocksOfType<IMyRemoteControl>(templist, (b) => b.CustomName.Contains(GroupName));
				strBlockCheck += "\nRemCons: " + templist.Count;
				if (templist.Count == 0)
					return false;

				templist.Clear();
				gts.GetBlocksOfType<IMyRemoteControl>(templist, (b) => (b.CustomName.Contains(GroupName) && b.IsFunctional));
				strBlockCheck += "   functional: " + templist.Count;
				if (templist.Count == 0)
					return false;

				//---------- BATTERY ------------
				templist.Clear();
				gts.GetBlocksOfType<IMyBatteryBlock>(templist, (b) => b.CustomName.Contains(GroupName));
				strBlockCheck += "\nBatteries: " + templist.Count;
				if (templist.Count == 0)
					return false;

				templist.Clear();
				gts.GetBlocksOfType<IMyBatteryBlock>(templist, (b) => (b.CustomName.Contains(GroupName) && b.IsFunctional));
				strBlockCheck += "   functional: " + templist.Count;
				if (templist.Count == 0)
					return false;

				//---------- THRUSTERS ------------
				templist.Clear();
				gts.GetBlocksOfType<IMyThrust>(templist, (b) => b.CustomName.Contains(GroupName));
				strBlockCheck += "\nThrusters: " + templist.Count;
				if (templist.Count == 0)
					return false;

				templist.Clear();
				gts.GetBlocksOfType<IMyThrust>(templist, (b) => (b.CustomName.Contains(GroupName) && b.IsFunctional));
				strBlockCheck += "   functional: " + templist.Count;
				if (templist.Count == 0)
					return false;

				//---------- GYROS ------------
				templist.Clear();
				gts.GetBlocksOfType<IMyGyro>(templist, (b) => b.CustomName.Contains(GroupName));
				strBlockCheck += "\nGyros: " + templist.Count;
				if (templist.Count == 0)
					return false;

				templist.Clear();
				gts.GetBlocksOfType<IMyGyro>(templist, (b) => (b.CustomName.Contains(GroupName) && b.IsFunctional));
				strBlockCheck += "   functional: " + templist.Count;
				if (templist.Count == 0)
					return false;

				//---------- WARHEADS ------------
				templist.Clear();
				gts.GetBlocksOfType<IMyWarhead>(templist, (b) => b.CustomName.Contains(GroupName));
				strBlockCheck += "\nWarheads: " + templist.Count;
				if (templist.Count == 0)
					return false;

				templist.Clear();
				gts.GetBlocksOfType<IMyWarhead>(templist, (b) => (b.CustomName.Contains(GroupName) && b.IsFunctional));
				strBlockCheck += "   functional: " + templist.Count;
				if (templist.Count == 0)
					return false;

				//---------- DECOYS ------------
				templist.Clear();
				gts.GetBlocksOfType<IMyDecoy>(templist, (b) => b.CustomName.Contains(GroupName));
				strBlockCheck += "\nDecoys: " + templist.Count;
				if (templist.Count == 0)
					return false;

				templist.Clear();
				gts.GetBlocksOfType<IMyDecoy>(templist, (b) => (b.CustomName.Contains(GroupName) && b.IsFunctional));
				strBlockCheck += "   functional: " + templist.Count;
				if (templist.Count == 0)
					return false;

				strBlockCheck += "\n-------------------------\n";
				return true;
			}

			public bool CheckIntegrity()
			{
				if (!remcon.IsFunctional)
					return false;
				if (batteries.FindAll((b) => (b.IsFunctional)).Count == 0)
					return false;
				if (thrusters.FindAll((b) => (b.IsFunctional)).Count == 0)
					return false;
				if (gyros.FindAll((b) => (b.IsFunctional)).Count == 0)
					return false;
				return true;
			}

			public void Launch(int VertDelay = 0)
			{
				if (VertDelay != 0)
					VerticalDelay = VertDelay;
				else
					VerticalDelay = LAUNCH_DELAY;

				foreach (var hydrogenTank in hydrogenTanks)
				{
					hydrogenTank.Stockpile = false;
				}

				//zostaje
				foreach (IMyBatteryBlock bat in batteries)
				{
					bat.Enabled = true;
					bat.ChargeMode = ChargeMode.Discharge;
				}
				//zostaje
				foreach (IMyGyro gyro in gyros)
				{
					gyro.Enabled = true;
					gyro.GyroOverride = true;
					gyro.Pitch = 0;
					gyro.Yaw = 0;
					gyro.Roll = 0;
				}
				foreach (IMyDecoy dec in decoys)
				{
					dec.Enabled = true;
				}

				if (WARHEAD_TIMER > 30)
					foreach (IMyWarhead warhead in warheads)
					{
						warhead.DetonationTime = WARHEAD_TIMER;
						warhead.StartCountdown();
					}
				merge.ApplyAction(Actions.TURN_OFF); //WYŁACZA MERGE BLOCK	
				connector.ToggleConnect(); //rOZŁACZA Connector
				status = 2;
			}

			public void Update(MyDetectedEntityInfo target, Vector3D T, Vector3D PointerPos, Vector3D PointerDir)
			{

				if (!Launched)
				{
					foreach (IMyThrust thr in thrusters)
					{
						thr.Enabled = true;
						thr.ThrustOverridePercentage = 1;
					}
					Launched = true;
				}
				else
				{
					counter++;
					if (remcon.IsFunctional && (counter > VerticalDelay))
					{
						double currentVelocity = remcon.GetShipVelocities().LinearVelocity.Length();
						Vector3D targetvector = new Vector3D();
						if (LASER_GUIDED)
						{
							targetvector = ((remcon.GetPosition() - PointerPos).Dot(PointerDir) + 700) * PointerDir + PointerPos - remcon.GetPosition();

						}
						else
						{
							targetvector = FindInterceptVector(remcon.GetPosition(),
																		currentVelocity * INTERCEPT_COURSE,
																		T,
																		target.Velocity);
						}

						Vector3D trgNorm = Vector3D.Normalize(targetvector);

						if ((target.Position - remcon.GetPosition()).Length() < WH_ARM_DIST)
						{
							if (currentVelocity - MyVelocity < -ACCEL_DET)
								foreach (IMyWarhead wh in warheads)
								{
									wh.Detonate();
								}

							MyVelocity = currentVelocity;
						}

						Vector3D velNorm = Vector3D.Normalize(remcon.GetShipVelocities().LinearVelocity);
						Vector3D CorrectionVector = Math.Max(ReflectK * trgNorm.Dot(velNorm), 1) * trgNorm - velNorm;
						Vector3D G = remcon.GetNaturalGravity();

						if (G.LengthSquared() == 0)
						{
							CorrectionVector = Math.Max(ReflectK * trgNorm.Dot(velNorm), 1) * trgNorm - velNorm;
						}
						else
						{
							if (JAVELIN)
							{
								//trgNorm = Vector3D.Normalize(Vector3D.Reflect(-G, trgNorm));
								trgNorm = Vector3D.Normalize(G.Dot(trgNorm) * trgNorm * JAVELIN_CURVE - G);
							}
							CorrectionVector = Math.Max(ReflectK * trgNorm.Dot(velNorm), 1) * trgNorm - velNorm;
							double A = 0;
							foreach (IMyThrust thr in thrusters)
							{
								A += thr.MaxEffectiveThrust;
							}
							A /= remcon.CalculateShipMass().PhysicalMass;

							Vector3D CorrectionNorm = Vector3D.Normalize(CorrectionVector);
							//CorrectionVector = CorrectionNorm * A - G;
							Vector3D gr = Vector3D.Reject(remcon.GetNaturalGravity(), CorrectionNorm);
							CorrectionVector = CorrectionNorm * Math.Sqrt(A * A + gr.LengthSquared()) - gr;
						}


						Vector3D Axis = Vector3D.Normalize(CorrectionVector).Cross(remcon.WorldMatrix.Forward);
						if (Axis.LengthSquared() < 0.1)
							Axis += remcon.WorldMatrix.Backward * ROLL;
						Axis *= GyroMult;
						foreach (IMyGyro gyro in gyros)
						{
							gyro.Pitch = (float)Axis.Dot(gyro.WorldMatrix.Right);
							gyro.Yaw = (float)Axis.Dot(gyro.WorldMatrix.Up);
							gyro.Roll = (float)Axis.Dot(gyro.WorldMatrix.Backward);
						}
					}
					else
					{
						foreach (IMyGyro gyro in gyros)
						{
							gyro.Pitch = 0;
							gyro.Yaw = 0;
							gyro.Roll = 0;
						}
					}

				}
			}

			private Vector3D FindInterceptVector(Vector3D shotOrigin, double shotSpeed,
				Vector3D targetOrigin, Vector3D targetVel)
			{
				Vector3D dirToTarget = Vector3D.Normalize(targetOrigin - shotOrigin);
				Vector3D targetVelOrth = Vector3D.Dot(targetVel, dirToTarget) * dirToTarget;
				Vector3D targetVelTang = targetVel - targetVelOrth;
				Vector3D shotVelTang = targetVelTang;
				double shotVelSpeed = shotVelTang.Length();

				if (shotVelSpeed > shotSpeed)
				{
					return Vector3D.Normalize(targetVel) * shotSpeed;
				}
				else
				{
					double shotSpeedOrth = Math.Sqrt(shotSpeed * shotSpeed - shotVelSpeed * shotVelSpeed);
					Vector3D shotVelOrth = dirToTarget * shotSpeedOrth;
					return shotVelOrth + shotVelTang;
				}
			}

			private void SetGyro(Vector3D dir)
			{

			}

			public void SwitchConnectorLock()
			{
				if (connector.Status == MyShipConnectorStatus.Connectable)
					connector.ToggleConnect();
			}

		}


		public class Radar
		{
			private List<IMyTerminalBlock> CamArray; //массив камер 
			private int CamIndex; //индекс текущей камеры в массиве 
			public MyDetectedEntityInfo CurrentTarget; // структура инфы о захваченном объекте 
			public Vector3D MyPos; // координаты 1й камеры (они и будут считаться нашим положением) 
			public Vector3D correctedTargetLocation; //расчетные координаты захваченного объекта. (прежние координаты+вектор скорости * прошедшее время с последнего обновления захвата) 
			public double TargetDistance; //расстояние до ведомой цели	 
			public int LastLockTick; // программный тик последнего обновления захвата 
			public int TicksPassed; // сколько тиков прошло с последнего обновления захвата 
			public bool Locked;
			public Vector3D T;//Координаты точки первого захвата
			public Vector3D O;//Координаты точки первого захвата лок


			public Radar(string groupname)
			{
				CamIndex = 0;
				Locked = false;
				CamArray = new List<IMyTerminalBlock>();
				IMyBlockGroup RadarGroup = gts.GetBlockGroupWithName(groupname);
				RadarGroup.GetBlocksOfType<IMyCameraBlock>(CamArray);
				foreach (IMyCameraBlock Cam in CamArray)
					Cam.EnableRaycast = true;
			}

			public void Lock(bool TryLock = false, double InitialRange = 10000)
			{
				int initCamIndex = CamIndex++;
				if (CamIndex >= CamArray.Count)
					CamIndex = 0;
				MyDetectedEntityInfo lastDetectedInfo;
				bool CanScan = true;
				// найдем первую после использованной в последний раз камеру, которая способна кастануть лучик на заданную дистанцию. 
				if (CurrentTarget.EntityId == 0)
					TargetDistance = InitialRange;

				while ((CamArray[CamIndex] as IMyCameraBlock)?.CanScan(TargetDistance) == false)
				{
					CamIndex++;
					if (CamIndex >= CamArray.Count)
						CamIndex = 0;
					if (CamIndex == initCamIndex)
					{
						CanScan = false;
						break;
					}
				}
				//если такая камера в массиве найдена - кастуем ей луч. 
				if (CanScan)
				{
					//в случае, если мы осуществляем первоначальный захват цели, кастуем луч вперед 
					if ((TryLock) && (CurrentTarget.IsEmpty()))
					{
						lastDetectedInfo = (CamArray[CamIndex] as IMyCameraBlock).Raycast(InitialRange, 0, 0);
						if ((!lastDetectedInfo.IsEmpty()) && (lastDetectedInfo.Relationship != MyRelationsBetweenPlayerAndBlock.Owner))
						{
							Locked = true;
							Vector3D deep_point = lastDetectedInfo.HitPosition.Value +
								Vector3D.Normalize(lastDetectedInfo.HitPosition.Value - CamArray[CamIndex].GetPosition()) * LOCK_POINT_DEPTH;
							O = WorldToGrid(lastDetectedInfo.HitPosition.Value, lastDetectedInfo.Position, lastDetectedInfo.Orientation);
						}
					}
					else //иначе - до координат предполагаемого нахождения цели.	 
						lastDetectedInfo = (CamArray[CamIndex] as IMyCameraBlock).Raycast(correctedTargetLocation);
					//если что-то нашли лучем, то захват обновлен	 
					if ((!lastDetectedInfo.IsEmpty()) && (lastDetectedInfo.Relationship != MyRelationsBetweenPlayerAndBlock.Owner))
					{
						Locked = true;
						CurrentTarget = lastDetectedInfo;
						LastLockTick = Tick;
						IMyTextPanel LCD = gts.GetBlockWithName("LCD") as IMyTextPanel;
						TicksPassed = 0;
					}
					else //иначе - захват потерян 
					{
						Locked = false;
						//CurrentTarget = lastDetectedInfo;
					}
				}
			}

			//этот метод сбрасывает захват цели 
			public void StopLock()
			{
				CurrentTarget = (CamArray[0] as IMyCameraBlock).Raycast(0, 0, 0);
			}

			// этот метод выводит данные по захваченному объекту на панель 

			public void Update()
			{
				MyPos = CamArray[0].GetPosition();
				//если в захвате находится какой-то объект, выполняем следующие действия 
				if (CurrentTarget.EntityId != 0)
				{
					TicksPassed = Tick - LastLockTick;
					//считаем предполагаемые координаты цели (прежние координаты + вектор скорости * прошедшее время с последнего обновления захвата) 
					if (CENTER_SHOT)
					{
						correctedTargetLocation = CurrentTarget.Position + (CurrentTarget.Velocity * TicksPassed / 60);
					}
					else
					{
						T = GridToWorld(O, CurrentTarget.Position, CurrentTarget.Orientation);
						correctedTargetLocation = T + (CurrentTarget.Velocity * TicksPassed / 60);
					}
					// добавим к дистанции до объекта 10 м (так просто для надежности) 
					TargetDistance = (correctedTargetLocation - MyPos).Length() + 10;

					//дальнейшее выполняется в случае, если пришло время обновить захват цели. Частота захвата в тиках считается как дистанция до объекта / 2000 * 60 / кол-во камер в массиве 
					// 2000 - это скорость восстановления дальности raycast по умолчанию) 
					// на 60 умножаем т.к. 2000 восстанавливается в сек, а в 1 сек 60 программных тиков 
					if (TicksPassed > TargetDistance * 0.03 / CamArray.Count)
					{
						Lock();
					}
				}
			}
		}

		public static Vector3D GridToWorld(Vector3 position, Vector3 GridPosition, MatrixD matrix)
		{
			double num1 = (position.X * matrix.M11 + position.Y * matrix.M21 + position.Z * matrix.M31);
			double num2 = (position.X * matrix.M12 + position.Y * matrix.M22 + position.Z * matrix.M32);
			double num3 = (position.X * matrix.M13 + position.Y * matrix.M23 + position.Z * matrix.M33);
			return new Vector3D(num1, num2, num3) + GridPosition;
		}
		public static Vector3D WorldToGrid(Vector3 world_position, Vector3 GridPosition, MatrixD matrix)
		{
			Vector3D position = world_position - GridPosition;
			double num1 = (position.X * matrix.M11 + position.Y * matrix.M12 + position.Z * matrix.M13);
			double num2 = (position.X * matrix.M21 + position.Y * matrix.M22 + position.Z * matrix.M23);
			double num3 = (position.X * matrix.M31 + position.Y * matrix.M32 + position.Z * matrix.M33);
			return new Vector3D(num1, num2, num3);
		}
		public static float TrimF(float Value, float Max, float Min)
		{
			return Math.Min(Math.Max(Value, Min), Max);
		}

		public static int VerticalDelay(float S, float T, int Delay0)
		{
			float V2 = Delay0 * MAX_VELOCITY / 60;
			float Tsqr = T * T;
			float Ssqr = S * S;
			float Vsqr = V2 * V2;
			float H = (float)Math.Sqrt(Ssqr + Vsqr);
			return (int)((-Tsqr * H
			- 2 * T * V2 * H + 2 * Ssqr * (T + V2) - Tsqr * T
			- 3 * Tsqr * V2 - 2 * T * Vsqr) / (2 * (Ssqr - Tsqr - 2 * T * V2) * (MAX_VELOCITY / 60)));
		}


		public static class Actions
		{
			public static string TURN_ON = "OnOff_On";
			public static string TURN_OFF = "OnOff_Off";
		}

		//=======================================================================
		//////////////////////////END////////////////////////////////////////////
		//=======================================================================

	}
}

