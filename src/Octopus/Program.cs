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
		//toolbar 
		/* 
		1st toolbar 
		Pause 
		camera1 view 
		camera2 view 
		blank 
		SaveConnector 
		StartDepoDrill 
		GoHome 
		StayHome 
		SetFlyHeight 
		*/

		int TickCount;
		int Clock = 10;
		string ShipName = "OctopusV1.2";
		string NewName = "OctopusV1.2";
		float GyroMult = 0.5f;
		int CriticalMass = 180000;
		float AlignAccelMult = 0.3f;
		float DrillAccel = 0.5f;
		float DrillSpeedLimit = 0.5f;
		float DrillGyroMult = 2f;
		float ReturnOnCharge = 0.25f;
		float DrillFrameWidth = 8f;
		float DrillFrameLength = 7f;
		float DrillDepth = 35;
		int MaxShafts = 50;
		float TargetSize = 100;
		int StoneDumpOn = 250000;
		//---------------------            


		IMyTimerBlock Timer;

		MyDriller thisDriller;
		public static class Commands
		{
			public const int Idle = 0;
			public const int SaveConnector = 1;
			public const int SaveDrillPoint = 2;
			public const int Lock = 3;
			public const int Complete = 4;
			public const int StartDepoDrill = 5;
			public const int DepoDrill = 6;
			public const int Pause = 7;
			public const int StartRockDrill = 8;
			public const int RockDrill = 9;

			public const int UnDock = 20;
			public const int ToDrillPoint = 21;
			public const int DrillAlign = 22;
			public const int Drill = 23;
			public const int PullOut = 24;
			public const int ToBase = 25;
			public const int Dock = 26;
			public const int Docked = 27;
			public const int BaseOperations = 28;
			public const int PullUp = 29;
		}

		void Main(string argument)
		{
			if (thisDriller == null)
				thisDriller = new MyDriller(ShipName, this);
			if (Timer == null)
				Timer = GridTerminalSystem.GetBlockWithName(thisDriller.MyName + "TimerClock") as IMyTimerBlock;
			TickCount++;

			if (argument != "")
			{
				TickCount = 0;
				switch (argument)
				{
					case "SaveConnector":
						{
							if (thisDriller.Paused)
								thisDriller.Pause();
							thisDriller.Command = Commands.SaveConnector;
							break;
						}
					case "StartDepoDrill":
						{
							if (thisDriller.Paused)
								thisDriller.Pause();
							thisDriller.Command = Commands.StartDepoDrill;
							break;
						}
					case "StartRockDrill":
						{
							if (thisDriller.Paused)
								thisDriller.Pause();
							thisDriller.Command = Commands.StartRockDrill;
							break;
						}
					case "Pause":
						{
							thisDriller.Pause();
							break;
						}
					case "Rename":
						{
							thisDriller.Rename(ShipName, NewName);
							if (!thisDriller.Paused)
								thisDriller.Pause();
							break;
						}
					case "PlanetCenter":
						{
							thisDriller.FindPlanetCenter();
							if (!thisDriller.Paused)
								thisDriller.Pause();
							break;
						}
					case "SetFlyHeight":
						{
							thisDriller.SetFlyHeight();
							if (!thisDriller.Paused)
								thisDriller.Pause();
							break;
						}
					case "DisplayHeight":
						{
							thisDriller.DisplayHeight();
							if (!thisDriller.Paused)
								thisDriller.Pause();
							break;
						}
					case "GoHome":
						{
							thisDriller.GoHome = true;
							if (thisDriller.Paused)
								thisDriller.Pause();
							break;
						}
					case "StayHome":
						{
							thisDriller.GoHome = true;
							thisDriller.StayHome = true;
							if (thisDriller.Paused)
								thisDriller.Pause();
							break;
						}

					default:
						break;
				}
			}

			if (!thisDriller.Paused)
			{
				if ((TickCount % Clock) == 0)
				{
					thisDriller.Update();
				}
				Timer.GetActionWithName("TriggerNow").Apply(Timer);
			}
		}

		public class MyDriller
		{
			private MyNavigation navBlock;
			private MyThrusters thrustBlock;
			private MyGyros gyroBlock;
			private MyCargo cargoBlock;
			private MyDrills drillBlock;
			private MyConnector connectorBlock;
			private MyBatteries batteryBlock;
			public int Command { get; set; }
			public string MyName { get; private set; }
			internal static Program ParentProgram;
			public float ShipMass { get; private set; }
			public bool EmergencyReturn = false;
			public bool GoHome = false;
			public bool StayHome = false;
			public bool Paused { get; private set; }
			public int CurrentStatus { get; set; }
			public bool Damaged = false;

			public MyDriller(string DrillerName, Program MyProg)
			{
				MyName = DrillerName;
				ParentProgram = MyProg;
				InitSubSystems();

				TextOutput("TPPaolo", "\n START \n");
				CheckDamaged();
			}
			public void InitSubSystems()
			{
				thrustBlock = new MyThrusters(this);
				gyroBlock = new MyGyros(this);
				cargoBlock = new MyCargo(this);
				drillBlock = new MyDrills(this);
				connectorBlock = new MyConnector(this);
				batteryBlock = new MyBatteries(this);
				navBlock = new MyNavigation(this);
			}

			public void FindPlanetCenter()
			{
				navBlock.FindPlanetCenter();
			}
			public void DisplayHeight()
			{
				navBlock.DisplayHeight();
			}
			public void SetFlyHeight()
			{
				navBlock.SetFlyHeight();
			}
			public void Rename(string OldName, string NewName)
			{
				List<IMyTerminalBlock> blockTemp = new List<IMyTerminalBlock>();
				ParentProgram.GridTerminalSystem.SearchBlocksOfName(OldName, blockTemp);
				for (int i = 0; i < blockTemp.Count; i++)
				{
					IMyTerminalBlock block = blockTemp[i] as IMyTerminalBlock;
					block.CustomName = block.CustomName.Replace(OldName, NewName);
				}
				MyName = NewName;
				InitSubSystems();
			}
			public void TextOutput(string TP, string Output = "")
			{
				IMyTextPanel ScrObj = ParentProgram.GridTerminalSystem.GetBlockWithName(MyName + TP) as IMyTextPanel;
				if (ScrObj != null)
				{
					if (Output != "")
					{
						ScrObj.WritePublicText(Output);
					}
					ScrObj.GetActionWithName("OnOff_On").Apply(ScrObj);
				}
			}
			private static void TurnGroup(string t, string OnOff)
			{
				var GrItems = GetBlocksFromGroup(t);
				for (int i = 0; i < GrItems.Count; i++)
				{
					var GrItem = GrItems[i] as IMyTerminalBlock;
					GrItem.GetActionWithName("OnOff_" + OnOff).Apply(GrItem);
				}
			}
			private static List<IMyTerminalBlock> GetBlocksFromGroup(string group)
			{
				var blocks = new List<IMyTerminalBlock>();
				ParentProgram.GridTerminalSystem.SearchBlocksOfName(group, blocks);
				if (blocks != null)
				{ return blocks; }
				throw new Exception("GetBlocksFromGroup: Group \"" + group + "\" not found");
			}

			public void CheckDamaged()
			{
				var dmg = IsDamaged();
				if (dmg)
				{
					GoHome = true;
					StayHome = true;
					Damaged = true;
					if (Paused)
						Pause();

					TextOutput("TPPaolo", "\n DAMAGED \n");
				}
				else if (Damaged)
				{
					//Repaired 
					GoHome = false;
					StayHome = false;
				}
			}

			//Paolo True if any named block is damaged 
			public bool IsDamaged()
			{
				List<IMyTerminalBlock> blockTemp = new List<IMyTerminalBlock>();
				ParentProgram.GridTerminalSystem.SearchBlocksOfName(MyName, blockTemp);
				for (int i = 0; i < blockTemp.Count; i++)
				{
					IMyTerminalBlock terminalBlock = blockTemp[i] as IMyTerminalBlock;
					IMySlimBlock slimBlock = terminalBlock.CubeGrid.GetCubeBlock(terminalBlock.Position);
					if (slimBlock == null) continue;

					bool damaged = (slimBlock.MaxIntegrity > slimBlock.BuildIntegrity) || slimBlock.CurrentDamage > 0;

					if (damaged)
					{
						string text = "\n Damaged: " + slimBlock.CurrentDamage.ToString() + " \n";
						text += "MaxIntegrity: " + slimBlock.MaxIntegrity.ToString() + " \n";
						text += "BuildIntegrity: " + slimBlock.BuildIntegrity.ToString() + " \n";
						text += "" + terminalBlock.CustomName.ToString() + " \n";
						text += "Count: " + blockTemp.Count.ToString() + " \n";

						TextOutput("TPPaolo", text);

						return damaged;
					}
				}

				TextOutput("TPPaolo", "Damaged: " + Damaged.ToString() + "\n Counter: " + navBlock.counter.ToString() + "\n");

				return false;
			}

			public void Pause()
			{
				IMyTextPanel ScrObj = ParentProgram.GridTerminalSystem.GetBlockWithName(MyName + "TP_Icon") as IMyTextPanel;
				if (Paused)
				{
					ParentProgram.Timer.GetActionWithName("Start").Apply(ParentProgram.Timer);
					Paused = false;
					ScrObj.GetActionWithName("OnOff_On").Apply(ScrObj);
				}
				else
				{
					gyroBlock.SetOverride(false, "", 1);
					thrustBlock.SetOverridePercent("", 0);
					drillBlock.Turn("Off");
					ParentProgram.Timer.GetActionWithName("Stop").Apply(ParentProgram.Timer);
					connectorBlock.Turn("On");
					Paused = true;
					ScrObj.GetActionWithName("OnOff_Off").Apply(ScrObj);
				}
				navBlock.SaveToStorage();
			}

			private void SequencerDepoDrill()
			{

				if (Command == Commands.StartDepoDrill)
				{
					navBlock.SetDrillMatrixDepo();
					CurrentStatus = Commands.DrillAlign;
					Command = Commands.DepoDrill;
					GoHome = false; StayHome = false;
				}
				else
					CheckDamaged(); //paolo check if damaged, if so sets stay at home. 
									//ll++; 
									//TextOutput("TPPaolo", ll.ToString() + "\n" );  
				switch (CurrentStatus)
				{
					case Commands.UnDock:
						if (navBlock.UnDock())
						{
							CurrentStatus = Commands.ToDrillPoint;
							navBlock.SaveToStorage();
						}
						break;
					case Commands.ToDrillPoint:
						if (GoHome)
						{
							CurrentStatus = Commands.ToBase;
							navBlock.SaveToStorage();
						}
						else if (navBlock.ToDrillPoint())
						{
							CurrentStatus = Commands.DrillAlign;
							navBlock.SaveToStorage();
						}
						break;
					case Commands.DrillAlign:
						if (GoHome)
						{
							CurrentStatus = Commands.ToBase;
							navBlock.SaveToStorage();
						}
						else if (navBlock.DrillAlign(out EmergencyReturn))
						{
							CurrentStatus = Commands.Drill;
							navBlock.SaveToStorage();
						}
						break;
					case Commands.Drill:
						if (GoHome)
						{
							CurrentStatus = Commands.PullOut;
							navBlock.SaveToStorage();
						}
						else if (navBlock.Drill(out EmergencyReturn))
						{
							if (navBlock.PullUpNeeded)
								CurrentStatus = Commands.PullUp;
							else
								CurrentStatus = Commands.PullOut;
							navBlock.SaveToStorage();
						}
						break;
					case Commands.PullUp:
						if (navBlock.PullUp())
						{
							CurrentStatus = Commands.Drill;
							navBlock.SaveToStorage();
						}
						break;
					case Commands.PullOut:
						if (navBlock.PullOut())
						{
							if (EmergencyReturn || GoHome)
								CurrentStatus = Commands.ToBase;
							else
							{
								navBlock.SetNewShaft();
								if (navBlock.ShaftN >= ParentProgram.MaxShafts)
									CurrentStatus = Commands.ToBase;
								else
									CurrentStatus = Commands.DrillAlign;
							}
							navBlock.SaveToStorage();
						}
						break;
					case Commands.ToBase:
						if (navBlock.ToBase())
						{
							CurrentStatus = Commands.Dock;
							navBlock.SaveToStorage();
						}
						break;
					case Commands.Dock:
						if (navBlock.Dock())
						{
							CurrentStatus = Commands.Docked;
							//navBlock.SaveToStorage();            
						}
						break;
					case Commands.Docked:
						if (connectorBlock.Locked)
						{
							CurrentStatus = Commands.BaseOperations;
							navBlock.SaveToStorage();
						}
						break;
					case Commands.BaseOperations:
						if (navBlock.UnloadAndRecharge())
						{
							if ((navBlock.ShaftN >= ParentProgram.MaxShafts) || StayHome)
							{
								CurrentStatus = Commands.Complete;
								batteryBlock.Recharge(false);
								thrustBlock.Turn("On");
								thrustBlock.SetOverridePercent("U", 0);
								thrustBlock.SetOverridePercent("R", 0);
								thrustBlock.SetOverridePercent("L", 0);
								thrustBlock.SetOverridePercent("F", 0);
								thrustBlock.SetOverridePercent("B", 0);
							}
							else
								CurrentStatus = Commands.UnDock;
							if (GoHome) GoHome = false;
							navBlock.SaveToStorage();
						}
						break;
					default:
						break;
				}
			}
			private void SequencerRockDrill()
			{
				if (Command == Commands.StartRockDrill)
				{

					navBlock.StartRockScavenging();
					CurrentStatus = Commands.ToDrillPoint;
					Command = Commands.RockDrill;
					GoHome = false; StayHome = false;
				}
				else
					switch (CurrentStatus)
					{
						case Commands.UnDock:
							if (navBlock.UnDock())
							{
								CurrentStatus = Commands.ToDrillPoint;
								navBlock.SaveToStorage();
							}
							break;
						case Commands.ToDrillPoint:
							if (GoHome)
							{
								CurrentStatus = Commands.ToBase;
								navBlock.SaveToStorage();
							}
							else if (navBlock.ToDrillPoint())
							{
								CurrentStatus = Commands.DrillAlign;
								navBlock.SaveToStorage();
							}
							break;
						case Commands.DrillAlign:
							if (GoHome)
							{
								CurrentStatus = Commands.ToBase;
								navBlock.SaveToStorage();
							}
							else if (navBlock.DrillAlign(out EmergencyReturn))
							{
								CurrentStatus = Commands.Drill;
								navBlock.SaveToStorage();
							}
							break;
						case Commands.Drill:
							if (GoHome)
							{
								CurrentStatus = Commands.PullOut;
								navBlock.SaveToStorage();
							}
							else if (navBlock.Drill(out EmergencyReturn))
							{
								if (navBlock.PullUpNeeded)
									CurrentStatus = Commands.PullUp;
								else
									CurrentStatus = Commands.PullOut;
								navBlock.SaveToStorage();
							}
							break;
						case Commands.PullUp:
							if (navBlock.PullUp())
							{
								CurrentStatus = Commands.Drill;
								navBlock.SaveToStorage();
							}
							break;
						case Commands.PullOut:
							if (navBlock.PullOut())
							{
								if (EmergencyReturn || GoHome)
									CurrentStatus = Commands.ToBase;
								else
								{
									if (navBlock.ShaftN >= 3)
									{
										navBlock.SetNewRock();
										if (navBlock.RockN >= navBlock.MaxRocks)
											CurrentStatus = Commands.ToBase;
										else
										{
											CurrentStatus = Commands.ToDrillPoint;
										}
									}
									else
									{
										navBlock.SetNewShaftRock();
										CurrentStatus = Commands.DrillAlign;
									}
								}
								navBlock.SaveToStorage();

							}
							break;
						case Commands.ToBase:
							if (navBlock.ToBase())
							{
								CurrentStatus = Commands.Dock;
								navBlock.SaveToStorage();
							}
							break;
						case Commands.Dock:
							if (navBlock.Dock())
							{
								CurrentStatus = Commands.Docked;
								//navBlock.SaveToStorage();            
							}
							break;
						case Commands.Docked:
							if (connectorBlock.Locked)
							{
								CurrentStatus = Commands.BaseOperations;
								navBlock.SaveToStorage();
							}
							break;
						case Commands.BaseOperations:
							if (navBlock.UnloadAndRecharge())
							{
								if ((navBlock.RockN >= navBlock.MaxRocks) || StayHome)
								{
									CurrentStatus = Commands.Complete;
									batteryBlock.Recharge(false);
									thrustBlock.Turn("On");
									thrustBlock.SetOverridePercent("U", 0);
									thrustBlock.SetOverridePercent("R", 0);
									thrustBlock.SetOverridePercent("L", 0);
									thrustBlock.SetOverridePercent("F", 0);
									thrustBlock.SetOverridePercent("B", 0);
								}
								else
									CurrentStatus = Commands.UnDock;
								if (GoHome) GoHome = false;
								navBlock.SaveToStorage();
							}
							break;
						default:
							break;
					}
			}

			public void Update()
			{
				if (!Paused)
				{
					cargoBlock.Update();
					navBlock.Update();
					thrustBlock.Update();
					batteryBlock.Update();

					if (Command == Commands.SaveConnector)
					{
						if (connectorBlock.Connector.Status == MyShipConnectorStatus.Connected)
						{
							navBlock.SetDockMatrix();
							navBlock.SaveToStorage();
							Command = Commands.Pause;
						}
					}
					else if ((Command == Commands.StartDepoDrill) || (Command == Commands.DepoDrill))
					{
						SequencerDepoDrill();
					}
					else if ((Command == Commands.StartRockDrill) || (Command == Commands.RockDrill))
					{
						SequencerRockDrill();
					}
					else if (Command == Commands.Pause)
					{
						Pause();
						navBlock.SaveToStorage();
					}
				}
			}

			private class MyThrusters
			{
				private List<IMyTerminalBlock> Thrusts;
				public float TotalMass { get; private set; }
				//public float ThrustMultiplier { get;private set;}            
				private MyDriller myDriller;
				private static string Prefix = "Thr";
				public float g { get; private set; }
				private IMyShipController ShipControl;

				private Matrix ControlLocM;

				public float UMaxT { get; private set; }
				public float DMaxT { get; private set; }
				public float FMaxT { get; private set; }
				public float BMaxT { get; private set; }
				public float RMaxT { get; private set; }
				public float LMaxT { get; private set; }

				public float XMaxA { get; private set; }
				public float YMaxA { get; private set; }
				public float ZMaxA { get; private set; }



				public MyThrusters(MyDriller mdr)
				{
					myDriller = mdr;
					Thrusts = new List<IMyTerminalBlock>();
					ShipControl = ParentProgram.GridTerminalSystem.GetBlockWithName(myDriller.MyName + "RemCon") as IMyShipController;
					ShipControl.Orientation.GetMatrix(out ControlLocM);
				}

				public void Update()
				{
					UMaxT = 0;
					DMaxT = 0;
					FMaxT = 0;
					BMaxT = 0;
					RMaxT = 0;
					LMaxT = 0;

					Matrix ThrLocM = new Matrix();

					ParentProgram.GridTerminalSystem.SearchBlocksOfName(myDriller.MyName + Prefix, Thrusts);
					for (int i = 0; i < Thrusts.Count; i++)
					{
						IMyThrust Thrust = Thrusts[i] as IMyThrust;
						if (Thrust != null)
						{
							Thrust.Orientation.GetMatrix(out ThrLocM);
							if (ThrLocM.Forward == ControlLocM.Up)
							{
								DMaxT += Thrust.MaxEffectiveThrust;
							}
							else if (ThrLocM.Forward == ControlLocM.Down)
							{
								UMaxT += Thrust.MaxEffectiveThrust;
							}
							else if (ThrLocM.Forward == ControlLocM.Forward)
							{
								BMaxT += Thrust.MaxEffectiveThrust;
							}
							else if (ThrLocM.Forward == ControlLocM.Backward)
							{
								FMaxT += Thrust.MaxEffectiveThrust;
							}
							else if (ThrLocM.Forward == ControlLocM.Right)
							{
								LMaxT += Thrust.MaxEffectiveThrust;
							}
							else if (ThrLocM.Forward == ControlLocM.Left)
							{
								RMaxT += Thrust.MaxEffectiveThrust;
							}
						}
					}

					g = (float)myDriller.navBlock.GravVector.Length();
					TotalMass = ShipControl.CalculateShipMass().PhysicalMass;
					//myDriller.TextOutput("TP_Rock", TotalMass.ToString());  
					YMaxA = Math.Min(UMaxT / TotalMass - g, DMaxT / TotalMass + g);
					ZMaxA = Math.Min(FMaxT, BMaxT) / TotalMass;
					XMaxA = Math.Min(RMaxT, LMaxT) / TotalMass;
				}

				public void SetOverridePercent(string axis, float OverrideValue)
				{
					ParentProgram.GridTerminalSystem.SearchBlocksOfName(myDriller.MyName + Prefix + axis, Thrusts);
					for (int i = 0; i < Thrusts.Count; i++)
					{
						IMyThrust Thrust = Thrusts[i] as IMyThrust;
						if (Thrust != null)
						{
							Thrust.SetValue("Override", OverrideValue);
							// ThrustMultiplier = Thrust.GetValue<float>("ThrustMultiplier");            
						}
					}
				}

				public void SetOverrideN(string axis, float OverrideValue)
				{
					float MaxThrust = 0;
					ParentProgram.GridTerminalSystem.SearchBlocksOfName(myDriller.MyName + Prefix + axis, Thrusts);

					if (axis == "U") MaxThrust = UMaxT;
					else if (axis == "D") MaxThrust = DMaxT;
					else if (axis == "F") MaxThrust = FMaxT;
					else if (axis == "B") MaxThrust = BMaxT;
					else if (axis == "R") MaxThrust = RMaxT;
					else if (axis == "L") MaxThrust = LMaxT;

					for (int i = 0; i < Thrusts.Count; i++)
					{
						IMyThrust Thrust = Thrusts[i] as IMyThrust;
						if (Thrust != null)
						{
							//if (axis == "U") myDriller.TextOutput("TP_Rock", OverrideValue.ToString());  
							if (OverrideValue == 0)
							{
								Thrust.SetValue("Override", 0);
							}
							else
							{
								Thrust.SetValue("Override", Math.Max(OverrideValue * 100 / MaxThrust, 2));
							}
						}
					}
				}

				public void SetOverrideAccel(string axis, float OverrideValue)
				{
					switch (axis)
					{

						case "U":
							OverrideValue += g;
							break;
						case "L":
							if (OverrideValue < 0)
							{
								axis = "R";
								OverrideValue = -OverrideValue;
							}
							break;
						case "R":
							if (OverrideValue < 0)
							{
								axis = "L";
								OverrideValue = -OverrideValue;
							}
							break;
						case "F":
							if (OverrideValue < 0)
							{
								axis = "B";
								OverrideValue = -OverrideValue;
							}
							break;
						case "B":
							if (OverrideValue < 0)
							{
								axis = "F";
								OverrideValue = -OverrideValue;
							}
							break;
					}
					SetOverrideN(axis, OverrideValue * TotalMass);
					//myDriller.TextOutput("TP_Rock", OverrideValue.ToString());            
				}

				public void Turn(string OnOff)
				{
					TurnGroup(myDriller.MyName + "Thr", OnOff);
				}
			}

			private class MyGyros
			{
				private MyDriller myDriller;
				private static string Prefix = "Gyro";

				public MyGyros(MyDriller mdr)
				{
					myDriller = mdr;
				}

				public void Turn(string OnOff)
				{
					TurnGroup(myDriller.MyName + Prefix, OnOff);
				}
				public void SetOverride(bool OverrideOnOff = true, string axis = "", float OverrideValue = 0, float Power = 1)
				{
					var Gyros = new List<IMyTerminalBlock>();
					ParentProgram.GridTerminalSystem.SearchBlocksOfName(myDriller.MyName + Prefix, Gyros);
					for (int i = 0; i < Gyros.Count; i++)
					{
						IMyGyro Gyro = Gyros[i] as IMyGyro;
						if (Gyro != null)
						{
							if (((!Gyro.GyroOverride) && OverrideOnOff) || ((Gyro.GyroOverride) && !OverrideOnOff))
								Gyro.ApplyAction("Override");

							Gyro.SetValue("Power", Power);
							if (axis != "")
								Gyro.SetValue(axis, OverrideValue);
						}
					}
				}
				public void SetOverride(bool OverrideOnOff, Vector3 settings, float Power = 1)
				{
					var Gyros = new List<IMyTerminalBlock>();
					ParentProgram.GridTerminalSystem.SearchBlocksOfName(myDriller.MyName + Prefix, Gyros);
					for (int i = 0; i < Gyros.Count; i++)
					{
						IMyGyro Gyro = Gyros[i] as IMyGyro;
						if (Gyro != null)
						{
							if ((!Gyro.GyroOverride) && OverrideOnOff)
								Gyro.ApplyAction("Override");
							Gyro.SetValue("Power", Power);
							Gyro.SetValue("Yaw", settings.GetDim(0));
							Gyro.SetValue("Pitch", settings.GetDim(1));
							Gyro.SetValue("Roll", settings.GetDim(2));
						}
					}
				}
			}

			private class MyNavigation
			{
				private MyDriller myDriller;
				private IMyRemoteControl RemCon;
				public double AbsHeight { get; private set; }
				public Vector3D MyPos { get; private set; }
				public Vector3D MyPrevPos { get; private set; }
				public Vector3D VelocityVector { get; private set; }
				public Vector3D UpVelocityVector { get; private set; }
				public Vector3D ForwVelocityVector { get; private set; }
				public Vector3D LeftVelocityVector { get; private set; }
				public Vector3D GravVector { get; private set; }
				public Vector3D PlanetCenter;
				public MatrixD DockMatrix { get; private set; }
				public MatrixD DrillMatrix { get; private set; }
				private Vector3D ConnectorPoint = new Vector3D(0, 0, 3);
				private Vector3D DrillPoint = new Vector3D(0, 0, 0);
				private double FlyHeight;
				private Vector3D BaseDockPoint;
				public int ShaftN { get; private set; }
				public int RockN { get; private set; }
				public int MaxRocks { get; private set; }
				public int Status { get; private set; }
				public bool PullUpNeeded { get; private set; }

				public int counter = 0;

				public MyNavigation(MyDriller mdr)
				{
					List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
					myDriller = mdr;
					RemCon = ParentProgram.GridTerminalSystem.GetBlockWithName(myDriller.MyName + "RemCon") as IMyRemoteControl;
					LoadFromStorage();
					FindPlanetCenter();
				}
				public void Update()
				{
					MyPrevPos = MyPos;
					MyPos = RemCon.GetPosition();
					GravVector = RemCon.GetNaturalGravity();
					VelocityVector = (MyPos - MyPrevPos) * 60 / ParentProgram.Clock;
					UpVelocityVector = RemCon.WorldMatrix.Up * Vector3D.Dot(VelocityVector, RemCon.WorldMatrix.Up);
					ForwVelocityVector = RemCon.WorldMatrix.Forward * Vector3D.Dot(VelocityVector, RemCon.WorldMatrix.Forward);
					LeftVelocityVector = RemCon.WorldMatrix.Left * Vector3D.Dot(VelocityVector, RemCon.WorldMatrix.Left);
					AbsHeight = (MyPos - PlanetCenter).Length();
				}
				public double GetVal(string Key, IMyTextPanel ScrObj)
				{
					string val = "0";
					string pattern = @"(" + Key + "):([^:^;]+);";
					System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(ScrObj.GetPublicText().Replace("\n", ""), pattern);
					if (match.Success)
					{
						val = match.Groups[2].Value;
					}
					return Convert.ToDouble(val);
				}
				public int GetValInt(string Key, IMyTextPanel ScrObj)
				{
					string val = "0";
					string pattern = @"(" + Key + "):([^:^;]+);";
					System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(ScrObj.GetPublicText().Replace("\n", ""), pattern);
					if (match.Success)
					{
						val = match.Groups[2].Value;
					}
					return Convert.ToInt32(val);
				}
				public Vector3D GetRockByNum(int Key, IMyTextPanel ScrObj)
				{
					string pattern = @"\n(" + Key.ToString() + ");([^;^;]+);([^;^;]+);([^;^;]+);";
					Vector3D RockPos = new Vector3D(0, 0, 0);
					System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(ScrObj.GetPublicText(), pattern);
					if (match.Success)
					{
						RockPos = new Vector3D(Convert.ToDouble(match.Groups[2].Value), Convert.ToDouble(match.Groups[3].Value), Convert.ToDouble(match.Groups[4].Value));
					}
					return RockPos;
				}
				public void FindPlanetCenter()
				{
					if ((RemCon as IMyShipController).TryGetPlanetPosition(out PlanetCenter))
					{
						myDriller.TextOutput("TP_Status", "Calibration: \n Planet Center: \n X: " + PlanetCenter.GetDim(0).ToString() + "\n Y: " + PlanetCenter.GetDim(1).ToString() + "\n Z: " + PlanetCenter.GetDim(2).ToString() + "\n");
						SaveToStorage();
					}
					else
					{
						myDriller.TextOutput("TP_Status", "Paolo: No planet center!\n");
					}
				}
				public void SetFlyHeight()
				{
					FlyHeight = (RemCon.GetPosition() - PlanetCenter).Length();
					BaseDockPoint = new Vector3D(0, 0, -200);
					SaveToStorage();
				}
				public void DisplayHeight()
				{
					myDriller.TextOutput("TP_Status", "Calibration: \n Planet Center: \n X: " + PlanetCenter.GetDim(0).ToString() + "\n Y: " + PlanetCenter.GetDim(1).ToString() + "\n Z: " + PlanetCenter.GetDim(2).ToString() + "\n" + Math.Round((RemCon.GetPosition() - PlanetCenter).Length(), 2).ToString() + "\n");
				}
				public void LoadFromStorage()
				{
					IMyTextPanel ScrObj = ParentProgram.GridTerminalSystem.GetBlockWithName(myDriller.MyName + "TP_Mem") as IMyTextPanel;
					myDriller.CurrentStatus = GetValInt("Status", ScrObj);
					myDriller.Command = GetValInt("Command", ScrObj);
					FlyHeight = GetVal("FlyHeight", ScrObj);
					ShaftN = GetValInt("ShaftN", ScrObj);
					RockN = GetValInt("RockN", ScrObj);
					MaxRocks = GetValInt("MaxRocks", ScrObj);
					myDriller.Paused = GetValInt("Paused", ScrObj) == 1;
					myDriller.GoHome = GetValInt("GoHome", ScrObj) == 1;
					myDriller.StayHome = GetValInt("StayHome", ScrObj) == 1;
					myDriller.EmergencyReturn = GetValInt("EmergencyReturn", ScrObj) == 1;
					if (myDriller.Command == Commands.DepoDrill)
						DrillPoint = GetSpiralXY(ShaftN, ParentProgram.DrillFrameWidth, ParentProgram.DrillFrameLength);
					else if (myDriller.Command == Commands.RockDrill)
					{
						DrillPoint = GetSpiralXY(ShaftN, ParentProgram.DrillFrameWidth, ParentProgram.DrillFrameLength) + new Vector3D(ParentProgram.DrillFrameWidth / 2, 12, -ParentProgram.DrillFrameLength / 2);
					}
					DockMatrix = new MatrixD(GetVal("MC11", ScrObj), GetVal("MC12", ScrObj), GetVal("MC13", ScrObj), GetVal("MC14", ScrObj),
					GetVal("MC21", ScrObj), GetVal("MC22", ScrObj), GetVal("MC23", ScrObj), GetVal("MC24", ScrObj),
					GetVal("MC31", ScrObj), GetVal("MC32", ScrObj), GetVal("MC33", ScrObj), GetVal("MC34", ScrObj),
					GetVal("MC41", ScrObj), GetVal("MC42", ScrObj), GetVal("MC43", ScrObj), GetVal("MC44", ScrObj));
					DrillMatrix = new MatrixD(GetVal("MD11", ScrObj), GetVal("MD12", ScrObj), GetVal("MD13", ScrObj), GetVal("MD14", ScrObj),
					GetVal("MD21", ScrObj), GetVal("MD22", ScrObj), GetVal("MD23", ScrObj), GetVal("MD24", ScrObj),
					GetVal("MD31", ScrObj), GetVal("MD32", ScrObj), GetVal("MD33", ScrObj), GetVal("MD34", ScrObj),
					GetVal("MD41", ScrObj), GetVal("MD42", ScrObj), GetVal("MD43", ScrObj), GetVal("MD44", ScrObj));
					PlanetCenter = new Vector3D(GetVal("PX", ScrObj), GetVal("PY", ScrObj), GetVal("PZ", ScrObj));

					BaseDockPoint = new Vector3D(0, 0, -200);
				}
				public void SaveToStorage()
				{
					IMyTextPanel ScrObj = ParentProgram.GridTerminalSystem.GetBlockWithName(myDriller.MyName + "TP_Mem") as IMyTextPanel;
					string NavData = "";
					NavData += "Command:" + myDriller.Command.ToString() + ";\n";
					NavData += "Status:" + myDriller.CurrentStatus.ToString() + ";\n";
					NavData += "Paused:" + (myDriller.Paused ? 1 : 0).ToString() + ";\n";
					NavData += "FlyHeight:" + Math.Round(FlyHeight, 0).ToString() + ";\n";
					NavData += "ShaftN:" + ShaftN.ToString() + ";\n";
					NavData += "RockN:" + RockN.ToString() + ";\n";
					NavData += "MaxRocks:" + MaxRocks.ToString() + ";\n";
					NavData += "EmergencyReturn:" + (myDriller.EmergencyReturn ? 1 : 0).ToString() + ";\n";
					NavData += "GoHome:" + (myDriller.GoHome ? 1 : 0).ToString() + ";\n";
					NavData += "StayHome:" + (myDriller.StayHome ? 1 : 0).ToString() + ";\n";
					NavData += DockMatrix.ToString().Replace("}", "").Replace("{", "").Replace(" ", " ").Replace(" ", ";\n").Replace("M", "MC");
					NavData += DrillMatrix.ToString().Replace("}", "").Replace("{", "").Replace(" ", " ").Replace(" ", ";\n").Replace("M", "MD");
					NavData += PlanetCenter.ToString().Replace("}", "").Replace("{", "").Replace(" ", " ").Replace(" ", ";\n").Replace("X", "PX").Replace("Y", "PY").Replace("Z", "PZ") + ";\n";

					ScrObj.ShowTextureOnScreen();
					ScrObj.WritePublicText(NavData);
					ScrObj.ShowPublicTextOnScreen();
					ScrObj.GetActionWithName("OnOff_On").Apply(ScrObj);
				}
				public MatrixD GetTransMatrixFromMyPos()
				{
					MatrixD mRot;
					Vector3D V3Dcenter = RemCon.GetPosition();
					Vector3D V3Dfow = RemCon.WorldMatrix.Forward;
					Vector3D V3Dup = RemCon.WorldMatrix.Up;
					Vector3D V3Dleft = RemCon.WorldMatrix.Left;
					mRot = new MatrixD(V3Dleft.GetDim(0), V3Dleft.GetDim(1), V3Dleft.GetDim(2), 0, V3Dup.GetDim(0), V3Dup.GetDim(1), V3Dup.GetDim(2), 0, V3Dfow.GetDim(0), V3Dfow.GetDim(1), V3Dfow.GetDim(2), 0, 0, 0, 0, 1);
					mRot = MatrixD.Invert(mRot);
					return new MatrixD(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -V3Dcenter.GetDim(0), -V3Dcenter.GetDim(1), -V3Dcenter.GetDim(2), 1) * mRot;
				}
				public MatrixD GetNormTransMatrixFromMyPos()
				{
					MatrixD mRot;
					Vector3D V3Dcenter = RemCon.GetPosition();
					Vector3D V3Dup = -Vector3D.Normalize(RemCon.GetNaturalGravity());
					Vector3D V3Dleft = Vector3D.Normalize(Vector3D.Reject(RemCon.WorldMatrix.Left, V3Dup));
					Vector3D V3Dfow = Vector3D.Normalize(Vector3D.Cross(V3Dleft, V3Dup));

					mRot = new MatrixD(V3Dleft.GetDim(0), V3Dleft.GetDim(1), V3Dleft.GetDim(2), 0, V3Dup.GetDim(0), V3Dup.GetDim(1), V3Dup.GetDim(2), 0, V3Dfow.GetDim(0), V3Dfow.GetDim(1), V3Dfow.GetDim(2), 0, 0, 0, 0, 1);
					mRot = MatrixD.Invert(mRot);
					return new MatrixD(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -V3Dcenter.GetDim(0), -V3Dcenter.GetDim(1), -V3Dcenter.GetDim(2), 1) * mRot;
				}
				public MatrixD GetNormTransMatrixFromPoint(Vector3D Point)
				{
					MatrixD mRot;
					Vector3D V3Dcenter = Point;
					Vector3D V3Dup = Vector3D.Normalize(V3Dcenter - PlanetCenter);
					Vector3D V3Dleft = Vector3D.Normalize(Vector3D.CalculatePerpendicularVector(V3Dup));
					Vector3D V3Dfow = Vector3D.Normalize(Vector3D.Cross(V3Dleft, V3Dup));

					mRot = new MatrixD(V3Dleft.GetDim(0), V3Dleft.GetDim(1), V3Dleft.GetDim(2), 0, V3Dup.GetDim(0), V3Dup.GetDim(1), V3Dup.GetDim(2), 0, V3Dfow.GetDim(0), V3Dfow.GetDim(1), V3Dfow.GetDim(2), 0, 0, 0, 0, 1);
					mRot = MatrixD.Invert(mRot);
					return new MatrixD(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -V3Dcenter.GetDim(0), -V3Dcenter.GetDim(1), -V3Dcenter.GetDim(2), 1) * mRot;
				}
				public int SetNewShaft()
				{
					ShaftN++;
					DrillPoint = GetSpiralXY(ShaftN, myDriller.drillBlock.Width, myDriller.drillBlock.Length);
					return ShaftN;
				}
				public void SetDockMatrix()
				{
					DockMatrix = GetNormTransMatrixFromMyPos();
				}

				public void SetDrillMatrixDepo()
				{
					DrillMatrix = GetNormTransMatrixFromMyPos();
					ShaftN = 0;
					DrillPoint = new Vector3D(0, 0, 0);
				}
				public void StartRockScavenging()
				{
					RockN = 0;
					ShaftN = 0;
					IMyTextPanel ScrObj = ParentProgram.GridTerminalSystem.GetBlockWithName(myDriller.MyName + "TP_Rock") as IMyTextPanel;
					MaxRocks = GetValInt("Rocks found", ScrObj);
					SetRock(RockN);
					SaveToStorage();
				}
				public void SetNewRock()
				{
					RockN++;
					ShaftN = 0;
					SetRock(RockN);
				}
				public void SetRock(int Num)
				{
					IMyTextPanel ScrObj = ParentProgram.GridTerminalSystem.GetBlockWithName(myDriller.MyName + "TP_Rock") as IMyTextPanel;
					DrillMatrix = GetNormTransMatrixFromPoint(GetRockByNum(Num, ScrObj));
					DrillPoint = new Vector3D(ParentProgram.DrillFrameWidth / 2, 14, -ParentProgram.DrillFrameLength / 2);
				}
				public int SetNewShaftRock()
				{
					ShaftN++;
					DrillPoint = GetSpiralXY(ShaftN, myDriller.drillBlock.Width, myDriller.drillBlock.Length);
					DrillPoint += new Vector3D(ParentProgram.DrillFrameWidth / 2, 14, -ParentProgram.DrillFrameLength / 2);
					return ShaftN;
				}
				public Vector3D GetNavAngles(Vector3D Target, MatrixD InvMatrix, double sfiftX = 0, double shiftZ = 0)
				{
					Vector3D V3Dcenter = RemCon.GetPosition();
					Vector3D V3Dfow = RemCon.WorldMatrix.Forward + V3Dcenter;
					Vector3D V3Dup = RemCon.WorldMatrix.Up + V3Dcenter;
					Vector3D V3Dleft = RemCon.WorldMatrix.Left + V3Dcenter;
					Vector3D GravNorm = Vector3D.Normalize(GravVector) + V3Dcenter;

					V3Dcenter = Vector3D.Transform(V3Dcenter, InvMatrix);
					V3Dfow = (Vector3D.Transform(V3Dfow, InvMatrix)) - V3Dcenter;
					V3Dup = (Vector3D.Transform(V3Dup, InvMatrix)) - V3Dcenter;
					V3Dleft = (Vector3D.Transform(V3Dleft, InvMatrix)) - V3Dcenter;
					GravNorm = Vector3D.Normalize((Vector3D.Transform(GravNorm, InvMatrix)) - V3Dcenter - new Vector3D(sfiftX, 0, shiftZ));

					Vector3D TargetNorm = Vector3D.Normalize(Vector3D.Reject(Target - V3Dcenter, GravNorm));

					double TargetPitch = Vector3D.Dot(V3Dfow, Vector3D.Normalize(Vector3D.Reject(-GravNorm, V3Dleft)));
					TargetPitch = Math.Acos(TargetPitch) - Math.PI / 2;

					double TargetRoll = Vector3D.Dot(V3Dleft, Vector3D.Reject(-GravNorm, V3Dfow));
					TargetRoll = Math.Acos(TargetRoll) - Math.PI / 2;

					double TargetYaw = Math.Acos(Vector3D.Dot(V3Dfow, TargetNorm));
					if ((V3Dleft - TargetNorm).Length() < Math.Sqrt(2))
						TargetYaw = -TargetYaw;

					if (double.IsNaN(TargetYaw)) TargetYaw = 0;
					if (double.IsNaN(TargetPitch)) TargetPitch = 0;
					if (double.IsNaN(TargetRoll)) TargetRoll = 0;

					return new Vector3D(TargetYaw, TargetPitch, TargetRoll);
				}
				public bool Dock()
				{
					bool Complete = false;
					float MaxUSpeed, MaxLSpeed, MaxFSpeed;
					Vector3D MyPosCon = Vector3D.Transform(MyPos, DockMatrix);
					Vector3D gyrAng = GetNavAngles(ConnectorPoint, DockMatrix);
					float Distance = (float)((Vector3D.Reject(MyPosCon, Vector3D.Normalize(Vector3D.Transform(PlanetCenter, DockMatrix)))).Length() + ConnectorPoint.Length());

					MaxLSpeed = (float)Math.Sqrt(2 * Math.Abs(MyPosCon.GetDim(0)) * myDriller.thrustBlock.XMaxA) / 2;
					MaxUSpeed = (float)Math.Sqrt(2 * Math.Abs(MyPosCon.GetDim(1)) * myDriller.thrustBlock.YMaxA) / 2;
					MaxFSpeed = (float)Math.Sqrt(2 * Distance * myDriller.thrustBlock.ZMaxA) / 2;
					if (Distance < 15)
						MaxFSpeed = MaxFSpeed / 5;
					if (Math.Abs(MyPosCon.GetDim(1)) < 1)
						MaxUSpeed = 0.1f;
					myDriller.gyroBlock.SetOverride(true, gyrAng * ParentProgram.GyroMult, 1);
					if (LeftVelocityVector.Length() < MaxLSpeed)
						myDriller.thrustBlock.SetOverrideAccel("R", (float)(MyPosCon.GetDim(0) * ParentProgram.AlignAccelMult));
					else
					{
						myDriller.thrustBlock.SetOverridePercent("R", 0);
						myDriller.thrustBlock.SetOverridePercent("L", 0);
					}
					float UpAccel = -(float)(MyPosCon.GetDim(1) * ParentProgram.AlignAccelMult);
					float minUpAccel = 0.3f;
					if ((UpAccel < 0) && (UpAccel > -minUpAccel))
						UpAccel = -minUpAccel;
					if ((UpAccel > 0) && (UpAccel < minUpAccel))
						UpAccel = minUpAccel;

					if (UpVelocityVector.Length() < MaxUSpeed)
						myDriller.thrustBlock.SetOverrideAccel("U", UpAccel);
					else
					{
						myDriller.thrustBlock.SetOverridePercent("U", 0);
					}
					if (((Distance > 100) || ((Math.Abs(MyPosCon.GetDim(0)) < (Distance / 10 + 0.2f)) && (Math.Abs(MyPosCon.GetDim(1)) < (Distance / 10 + 0.2f)))) && (ForwVelocityVector.Length() < MaxFSpeed))
					{
						myDriller.thrustBlock.SetOverrideAccel("F", (float)(Distance * ParentProgram.AlignAccelMult));
						myDriller.thrustBlock.SetOverridePercent("B", 0);
					}
					else
					{
						myDriller.thrustBlock.SetOverridePercent("F", 0);
						myDriller.thrustBlock.SetOverridePercent("B", 0);
					}
					if (Distance < 6)
					{
						myDriller.connectorBlock.Turn("On");
						if (myDriller.connectorBlock.Connector.Status == MyShipConnectorStatus.Connectable)
							Complete = myDriller.connectorBlock.Lock();
					}
					string strStatus = " STATUS\n";
					if (myDriller.Command == Commands.DepoDrill)
						strStatus += "Cycle: Ore field mining\n";
					else if (myDriller.Command == Commands.RockDrill)
						strStatus += "Cycle: Rock scavenging\n";
					strStatus += "Task: Docking \n";
					strStatus += "Ship Mass: " + myDriller.thrustBlock.TotalMass.ToString() + "\n";
					strStatus += "Battery charge: " + Math.Round(myDriller.batteryBlock.StoredPower * 100 / myDriller.batteryBlock.MaxPower, 2).ToString() + " % \n";
					strStatus += "XY shifts: " + Math.Round(MyPosCon.GetDim(0), 2).ToString() + " / " + Math.Round(MyPosCon.GetDim(1), 2).ToString() + "\n";
					strStatus += "Distance: " + Math.Round(Distance).ToString() + "\n";
					strStatus += "Connector: " + (Complete ? "Locked" : "Unlocked") + "\n";
					strStatus += "Shafts drilled: " + ShaftN.ToString() + " / " + ParentProgram.MaxShafts.ToString() + "\n";
					myDriller.TextOutput("TP_Status", strStatus);
					//return false;            
					return Complete;
				}
				public bool UnloadAndRecharge()
				{
					bool Complete = true;
					if (myDriller.cargoBlock.CurrentMass > 0)
					{
						myDriller.cargoBlock.UnLoad();
						Complete = false;
					}
					if ((myDriller.batteryBlock.MaxPower - myDriller.batteryBlock.StoredPower) > 0.5f)
					{
						myDriller.batteryBlock.Recharge(true);
						Complete = false;
					}
					else myDriller.batteryBlock.Recharge(false);
					if (!Complete)
					{
						if (myDriller.connectorBlock.Locked)
						{
							myDriller.thrustBlock.Turn("Off");
						}
					}
					else
					{
						myDriller.thrustBlock.Turn("On");
						myDriller.thrustBlock.SetOverridePercent("U", 0);
						myDriller.thrustBlock.SetOverridePercent("R", 0);
						myDriller.thrustBlock.SetOverridePercent("L", 0);
						myDriller.thrustBlock.SetOverridePercent("F", 0);
					}
					string strStatus = " STATUS\n";
					if (myDriller.Command == Commands.DepoDrill)
						strStatus += "Cycle: Ore field mining\n";
					else if (myDriller.Command == Commands.RockDrill)
						strStatus += "Cycle: Rock scavenging\n";
					strStatus += "Task: Unload & Recharge\n";
					strStatus += "Ship Mass: " + myDriller.thrustBlock.TotalMass.ToString() + "\n";
					strStatus += "Battery charge: " + Math.Round(myDriller.batteryBlock.StoredPower * 100 / myDriller.batteryBlock.MaxPower, 2).ToString() + " % \n";
					strStatus += "Shafts drilled: " + ShaftN.ToString() + " / " + ParentProgram.MaxShafts.ToString() + "\n";
					strStatus += "Connector: " + (myDriller.connectorBlock.Locked ? "Locked" : "Unlocked") + "\n";
					strStatus += "\n !WARNING \n";
					strStatus += "Do not disconnect!\n";
					myDriller.TextOutput("TP_Status", strStatus);
					//myDriller.TextOutput("TP_Status", Complete.ToString());            
					return Complete;
				}
				public bool UnDock()
				{
					bool Complete = false;
					float Distance = 0;
					if (myDriller.connectorBlock.UnLock())
					{
						Vector3D MyPosCon = Vector3D.Transform(MyPos, DockMatrix);
						Vector3D gyrAng = GetNavAngles(ConnectorPoint, DockMatrix);
						Distance = (float)((Vector3D.Reject(MyPosCon, Vector3D.Normalize(Vector3D.Transform(PlanetCenter, DockMatrix)))).Length() + ConnectorPoint.Length());
						myDriller.gyroBlock.SetOverride(true, gyrAng * ParentProgram.GyroMult, 1);
						myDriller.thrustBlock.SetOverridePercent("U", 0);
						myDriller.thrustBlock.SetOverridePercent("R", 0);
						myDriller.thrustBlock.SetOverridePercent("L", 0);
						myDriller.thrustBlock.SetOverridePercent("F", 0);
						myDriller.thrustBlock.SetOverrideAccel("B", 3);
						if (Distance > 50)
						{
							Complete = true;
						}
					}
					string strStatus = " STATUS\n";
					if (myDriller.Command == Commands.DepoDrill)
						strStatus += "Cycle: Ore field mining\n";
					else if (myDriller.Command == Commands.RockDrill)
						strStatus += "Cycle: Rock scavenging\n";
					strStatus += "Task: Undocking\n";
					strStatus += "Ship Mass: " + myDriller.thrustBlock.TotalMass.ToString() + "\n";
					strStatus += "Battery charge: " + Math.Round(myDriller.batteryBlock.StoredPower * 100 / myDriller.batteryBlock.MaxPower, 2).ToString() + " % \n";
					strStatus += "Connector: " + (myDriller.connectorBlock.Locked ? "Locked" : "Unlocked") + "\n";
					strStatus += "Shafts drilled: " + ShaftN.ToString() + " / " + ParentProgram.MaxShafts.ToString() + "\n";
					strStatus += "Distance: " + Math.Round(Distance).ToString() + "\n";
					myDriller.TextOutput("TP_Status", strStatus);
					return Complete;
				}
				public bool ToBase()
				{
					bool Complete = false;
					float MaxUSpeed, MaxFSpeed;
					Vector3D gyrAng = GetNavAngles(BaseDockPoint, DockMatrix);
					Vector3D MyPosCon = Vector3D.Transform(MyPos, DockMatrix);
					float Distance = (float)(BaseDockPoint - new Vector3D(MyPosCon.GetDim(0), 0, MyPosCon.GetDim(2))).Length();

					MaxUSpeed = (float)Math.Sqrt(2 * Math.Abs(FlyHeight - (MyPos - PlanetCenter).Length()) * myDriller.thrustBlock.YMaxA) / 1.2f;
					MaxFSpeed = (float)Math.Sqrt(2 * Distance * myDriller.thrustBlock.ZMaxA) / 1.2f;
					myDriller.gyroBlock.SetOverride(true, gyrAng * ParentProgram.GyroMult, 1);
					myDriller.thrustBlock.SetOverridePercent("R", 0);
					myDriller.thrustBlock.SetOverridePercent("L", 0);

					if (UpVelocityVector.Length() < MaxUSpeed)
						myDriller.thrustBlock.SetOverrideAccel("U", (float)((FlyHeight - (MyPos - PlanetCenter).Length()) * ParentProgram.AlignAccelMult));
					else
					{
						myDriller.thrustBlock.SetOverridePercent("U", 0);
					}
					// myDriller.TextOutput("TP1", "\n" + MaxFSpeed.ToString() + "\n" + ForwVelocityVector.Length().ToString() + "\n" + Distance.ToString());            
					if (Distance > ParentProgram.TargetSize)
					{
						if (ForwVelocityVector.Length() < MaxFSpeed)
						{
							myDriller.thrustBlock.SetOverrideAccel("F", (float)(Distance * ParentProgram.AlignAccelMult));
							myDriller.thrustBlock.SetOverridePercent("B", 0);
						}
						else
						{
							myDriller.thrustBlock.SetOverridePercent("F", 0);
							myDriller.thrustBlock.SetOverridePercent("B", 0);
						}
					}
					else
					{
						Complete = true;
					}
					string strStatus = " STATUS\n";
					if (myDriller.Command == Commands.DepoDrill)
						strStatus += "Cycle: Ore field mining\n";
					else if (myDriller.Command == Commands.RockDrill)
						strStatus += "Cycle: Rock scavenging\n";
					strStatus += "Task: To base\n";
					strStatus += "Battery charge: " + Math.Round(myDriller.batteryBlock.StoredPower * 100 / myDriller.batteryBlock.MaxPower, 2).ToString() + " % \n";
					strStatus += "Height: " + Math.Round((MyPos - PlanetCenter).Length()).ToString() + " / " + Math.Round(FlyHeight).ToString() + "\n";
					strStatus += "Distance: " + Math.Round(Distance).ToString() + "\n";
					strStatus += "Shafts drilled: " + ShaftN.ToString() + " / " + ParentProgram.MaxShafts.ToString() + "\n";
					myDriller.TextOutput("TP_Status", strStatus);
					return Complete;
				}

				public bool ToDrillPoint()
				{
					bool Complete = false;
					float MaxUSpeed, MaxFSpeed;
					Vector3D gyrAng = GetNavAngles(new Vector3D(0, 0, 0), DrillMatrix);
					Vector3D MyPosDrill = Vector3D.Transform(MyPos, DrillMatrix);
					float Distance = (float)(DrillPoint - new Vector3D(MyPosDrill.GetDim(0), 0, MyPosDrill.GetDim(2))).Length();

					MaxUSpeed = (float)Math.Sqrt(2 * Math.Abs(FlyHeight - (MyPos - PlanetCenter).Length()) * myDriller.thrustBlock.YMaxA) / 1.2f;
					MaxFSpeed = (float)Math.Sqrt(2 * Distance * myDriller.thrustBlock.ZMaxA) / 1.2f;
					myDriller.gyroBlock.SetOverride(true, gyrAng * ParentProgram.GyroMult, 1);
					myDriller.thrustBlock.SetOverridePercent("R", 0);
					myDriller.thrustBlock.SetOverridePercent("L", 0);

					if (UpVelocityVector.Length() < MaxUSpeed)
						myDriller.thrustBlock.SetOverrideAccel("U", (float)((FlyHeight - (MyPos - PlanetCenter).Length()) * ParentProgram.AlignAccelMult));
					else
					{
						myDriller.thrustBlock.SetOverridePercent("U", 0);
					}
					//myDriller.TextOutput("TP1", "\n" + MaxFSpeed.ToString() + "\n" + ForwVelocityVector.Length().ToString() + "\n" + Distance.ToString());            
					if (Distance > ParentProgram.TargetSize)
					{
						if (ForwVelocityVector.Length() < MaxFSpeed)
						{
							myDriller.thrustBlock.SetOverrideAccel("F", (float)(Distance * ParentProgram.AlignAccelMult));
							myDriller.thrustBlock.SetOverridePercent("B", 0);
						}
						else
						{
							myDriller.thrustBlock.SetOverridePercent("F", 0);
							myDriller.thrustBlock.SetOverridePercent("B", 0);
						}
					}
					else
					{
						Complete = true;
					}

					string strStatus = " STATUS\n";
					if (myDriller.Command == Commands.DepoDrill)
						strStatus += "Cycle: Ore field mining\n";
					else if (myDriller.Command == Commands.RockDrill)
						strStatus += "Cycle: Rock scavenging\n";

					strStatus += "Task: To drilling point\n";
					strStatus += "Battery charge: " + Math.Round(myDriller.batteryBlock.StoredPower * 100 / myDriller.batteryBlock.MaxPower, 2).ToString() + " % \n";
					strStatus += "Height: " + Math.Round((MyPos - PlanetCenter).Length()).ToString() + " / " + Math.Round(FlyHeight).ToString() + "\n";
					strStatus += "Distance: " + Math.Round(Distance).ToString() + "\n";
					strStatus += "Shafts drilled: " + ShaftN.ToString() + " / " + ParentProgram.MaxShafts.ToString() + "\n";
					myDriller.TextOutput("TP_Status", strStatus);
					return Complete;
				}

				public bool DrillAlign(out bool Emergency)
				{
					bool Complete = false;
					Emergency = false;
					float MaxUSpeed, MaxLSpeed, MaxFSpeed;
					Vector3D MyPosDrill = Vector3D.Transform(MyPos, DrillMatrix) - DrillPoint;

					Vector3D gyrAng = GetNavAngles(MyPosDrill + DrillPoint + new Vector3D(0, 0, 1), DrillMatrix);
					myDriller.gyroBlock.SetOverride(true, gyrAng * ParentProgram.GyroMult, 1);

					MaxLSpeed = (float)Math.Sqrt(2 * Math.Abs(MyPosDrill.GetDim(0)) * myDriller.thrustBlock.XMaxA) / 2;
					MaxUSpeed = (float)Math.Sqrt(2 * Math.Abs(MyPosDrill.GetDim(1)) * myDriller.thrustBlock.YMaxA) / 2;
					MaxFSpeed = (float)Math.Sqrt(2 * Math.Abs(MyPosDrill.GetDim(2)) * myDriller.thrustBlock.ZMaxA) / 2;
					if (Math.Abs(MyPosDrill.GetDim(1)) < 1)
						MaxUSpeed = 0.1f;
					if (LeftVelocityVector.Length() < MaxLSpeed)
						myDriller.thrustBlock.SetOverrideAccel("R", (float)(MyPosDrill.GetDim(0) * ParentProgram.AlignAccelMult));
					else
					{
						myDriller.thrustBlock.SetOverridePercent("R", 0);
						myDriller.thrustBlock.SetOverridePercent("L", 0);
					}
					if (ForwVelocityVector.Length() < MaxFSpeed)
						myDriller.thrustBlock.SetOverrideAccel("B", (float)(MyPosDrill.GetDim(2) * ParentProgram.AlignAccelMult));
					else
					{
						myDriller.thrustBlock.SetOverridePercent("F", 0);
						myDriller.thrustBlock.SetOverridePercent("B", 0);
					}
					if (UpVelocityVector.Length() < MaxUSpeed)
					{
						float UpAccel = -(float)(MyPosDrill.GetDim(1) * ParentProgram.AlignAccelMult);
						float minUpAccel = 0.3f;
						if ((UpAccel < 0) && (UpAccel > -minUpAccel))
							UpAccel = -minUpAccel;
						if ((UpAccel > 0) && (UpAccel < minUpAccel))
							UpAccel = minUpAccel;
						myDriller.thrustBlock.SetOverrideAccel("U", UpAccel);
					}
					else
					{
						myDriller.thrustBlock.SetOverridePercent("U", 0);
					}
					if (MyPosDrill.Length() < 0.5)
					{
						Complete = true;
					}
					else if (myDriller.cargoBlock.CriticalMassReached || myDriller.batteryBlock.LowPower)
					{
						Complete = true;
						Emergency = true;
					}

					counter++;

					if (counter > 2000)
					{
						Complete = true;
						counter = 0;
					}
					if (Complete)
					{
						counter = 0;
					}

					//myDriller.TextOutput("TP1", "\n" + ShaftN.ToString() + "\n" + DrillPoint.ToString() + "\n" + MyPosDrill.ToString());            

					string strStatus = " STATUS\n";
					if (myDriller.Command == Commands.DepoDrill)
						strStatus += "Cycle: Ore field mining\n";
					else if (myDriller.Command == Commands.RockDrill)
						strStatus += "Cycle: Rock scavenging\n";
					strStatus += "Task: Alligning \n";
					strStatus += "Ship Mass: " + myDriller.thrustBlock.TotalMass.ToString() + "\n";
					strStatus += "Battery charge: " + Math.Round(myDriller.batteryBlock.StoredPower * 100 / myDriller.batteryBlock.MaxPower, 2).ToString() + " % \n";
					strStatus += "Y shift: " + Math.Round(MyPosDrill.GetDim(1), 2).ToString() + "\n";
					strStatus += "XZ shifts: " + Math.Round(MyPosDrill.GetDim(0), 2).ToString() + " / " + Math.Round(MyPosDrill.GetDim(2), 2).ToString() + "\n";
					strStatus += "Shafts drilled: " + ShaftN.ToString() + " / " + ParentProgram.MaxShafts.ToString() + "\n";
					myDriller.TextOutput("TP_Status", strStatus);

					return Complete;
				}

				public bool Drill(out bool Emergency)
				{
					bool Complete = false;
					Emergency = false;

					float MaxLSpeed, MaxFSpeed;
					Vector3D MyPosDrill = Vector3D.Transform(MyPos, DrillMatrix) - DrillPoint;

					double shiftX = MyPosDrill.GetDim(0) / 2;
					if (shiftX < 0) shiftX = Math.Max(shiftX, -0.1);
					else shiftX = Math.Min(shiftX, 0.1);
					double shiftZ = MyPosDrill.GetDim(2) / 2;
					if (shiftZ < 0) shiftZ = Math.Max(shiftZ, -0.1);
					else shiftZ = Math.Min(shiftZ, 0.1);



					Vector3D gyrAng = GetNavAngles(MyPosDrill + DrillPoint + new Vector3D(0, 0, 1), DrillMatrix, shiftX, shiftZ);
					myDriller.gyroBlock.SetOverride(true, gyrAng * ParentProgram.DrillGyroMult, 1);


					MaxLSpeed = (float)Math.Sqrt(2 * Math.Abs(MyPosDrill.GetDim(0)) * myDriller.thrustBlock.XMaxA) / 5;
					MaxFSpeed = (float)Math.Sqrt(2 * Math.Abs(MyPosDrill.GetDim(2)) * myDriller.thrustBlock.ZMaxA) / 5;



					if (LeftVelocityVector.Length() < MaxLSpeed)
						myDriller.thrustBlock.SetOverrideAccel("R", (float)(MyPosDrill.GetDim(0) * 10));
					else
					{
						myDriller.thrustBlock.SetOverridePercent("R", 0);
						myDriller.thrustBlock.SetOverridePercent("L", 0);
					}
					if (ForwVelocityVector.Length() < MaxFSpeed)
						myDriller.thrustBlock.SetOverrideAccel("B", (float)(MyPosDrill.GetDim(2) * 10));
					else
					{
						myDriller.thrustBlock.SetOverridePercent("F", 0);
						myDriller.thrustBlock.SetOverridePercent("B", 0);
					}


					if (myDriller.cargoBlock.StoneDumpNeeded && myDriller.drillBlock.DrillSOn)
						myDriller.drillBlock.Turn("Off");
					else if (!myDriller.cargoBlock.StoneDumpNeeded && !myDriller.drillBlock.DrillSOn)
						myDriller.drillBlock.Turn("On");

					if ((UpVelocityVector.Length() < ParentProgram.DrillSpeedLimit) && (!myDriller.cargoBlock.StoneDumpNeeded))
					{
						if ((Math.Abs(MyPosDrill.GetDim(0)) < 1) && (Math.Abs(MyPosDrill.GetDim(2)) < 1))
							myDriller.thrustBlock.SetOverrideAccel("U", (-ParentProgram.DrillAccel));
						else
						{
							myDriller.thrustBlock.SetOverrideAccel("U", (ParentProgram.DrillAccel));
							PullUpNeeded = true;
							Complete = true;
						}
					}
					else
					{
						myDriller.thrustBlock.SetOverridePercent("U", 0);
					}
					if (MyPosDrill.GetDim(1) < ((myDriller.Command == Commands.DepoDrill) ? (-ParentProgram.DrillDepth) : -16))
						Complete = true;
					else if (myDriller.cargoBlock.CriticalMassReached || myDriller.batteryBlock.LowPower)
					{
						Complete = true;
						Emergency = true;
					}

					string strStatus = " STATUS\n";
					if (myDriller.Command == Commands.DepoDrill)
						strStatus += "Cycle: Ore field mining\n";
					else if (myDriller.Command == Commands.RockDrill)
						strStatus += "Cycle: Rock scavenging\n";
					strStatus += "Task: Drilling\n";
					strStatus += "Ship Mass: " + myDriller.thrustBlock.TotalMass.ToString() + "\n";
					strStatus += "Battery charge: " + Math.Round(myDriller.batteryBlock.StoredPower * 100 / myDriller.batteryBlock.MaxPower, 2).ToString() + " % \n";
					strStatus += "Depth: " + Math.Round(-MyPosDrill.GetDim(1), 2).ToString() + " / " + Math.Round(((myDriller.Command == Commands.DepoDrill) ? (ParentProgram.DrillDepth) : 16), 2).ToString() + "\n";
					strStatus += "XZ shifts: " + Math.Round(MyPosDrill.GetDim(0), 2).ToString() + " / " + Math.Round(MyPosDrill.GetDim(2), 2).ToString() + "\n";
					strStatus += "Shafts drilled: " + ShaftN.ToString() + " / " + ParentProgram.MaxShafts.ToString() + "\n";
					myDriller.TextOutput("TP_Status", strStatus);

					return Complete;
				}

				public bool PullUp()
				{
					bool Complete = false;
					float MaxLSpeed, MaxFSpeed;
					Vector3D MyPosDrill = Vector3D.Transform(MyPos, DrillMatrix) - DrillPoint;
					Vector3D gyrAng = GetNavAngles(MyPosDrill + DrillPoint + new Vector3D(0, 0, 1), DrillMatrix);
					myDriller.gyroBlock.SetOverride(true, gyrAng * ParentProgram.DrillGyroMult, 1);
					//myDriller.drillBlock.Turn("Off");            

					MaxLSpeed = (float)Math.Sqrt(2 * Math.Abs(MyPosDrill.GetDim(0)) * myDriller.thrustBlock.XMaxA) / 4;
					MaxFSpeed = (float)Math.Sqrt(2 * Math.Abs(MyPosDrill.GetDim(2)) * myDriller.thrustBlock.ZMaxA) / 4;


					if (LeftVelocityVector.Length() < MaxLSpeed)
						myDriller.thrustBlock.SetOverrideAccel("R", (float)(MyPosDrill.GetDim(0) * 0.5));
					else
					{
						myDriller.thrustBlock.SetOverridePercent("R", 0);
						myDriller.thrustBlock.SetOverridePercent("L", 0);
					}
					if (ForwVelocityVector.Length() < MaxFSpeed)
						myDriller.thrustBlock.SetOverrideAccel("B", (float)(MyPosDrill.GetDim(2) * 0.5));
					else
					{
						myDriller.thrustBlock.SetOverridePercent("F", 0);
						myDriller.thrustBlock.SetOverridePercent("B", 0);
					}

					if (UpVelocityVector.Length() < ParentProgram.DrillSpeedLimit * 5)
						myDriller.thrustBlock.SetOverrideAccel("U", (ParentProgram.DrillAccel * 2));
					else
					{
						myDriller.thrustBlock.SetOverridePercent("U", 0);
					}

					if ((MyPosDrill.GetDim(1) > 0) || ((MyPosDrill.GetDim(0) < 0.15) && (MyPosDrill.GetDim(2) < 0.15)))
					{
						Complete = true;
						PullUpNeeded = false;
					}
					string strStatus = " STATUS\n";
					if (myDriller.Command == Commands.DepoDrill)
						strStatus += "Cycle: Ore field mining\n";
					else if (myDriller.Command == Commands.RockDrill)
						strStatus += "Cycle: Rock scavenging\n";
					strStatus += "Task: Pull Up\n";
					strStatus += "Cargo Mass: " + myDriller.cargoBlock.CurrentMass.ToString() + "\n";
					strStatus += "Battery charge: " + Math.Round(myDriller.batteryBlock.StoredPower * 100 / myDriller.batteryBlock.MaxPower, 2).ToString() + " % \n";
					strStatus += "Depth: " + Math.Round(-MyPosDrill.GetDim(1), 2).ToString() + " / " + Math.Round(((myDriller.Command == Commands.DepoDrill) ? (ParentProgram.DrillDepth) : 16), 2).ToString() + "\n";
					strStatus += "XZ shifts: " + Math.Round(MyPosDrill.GetDim(0), 2).ToString() + " / " + Math.Round(MyPosDrill.GetDim(2), 2).ToString() + "\n";
					strStatus += "Shafts drilled: " + ShaftN.ToString() + " / " + ParentProgram.MaxShafts.ToString() + "\n";
					myDriller.TextOutput("TP_Status", strStatus);

					return Complete;
				}

				public bool PullOut()
				{
					bool Complete = false;
					float MaxLSpeed, MaxFSpeed;
					Vector3D MyPosDrill = Vector3D.Transform(MyPos, DrillMatrix) - DrillPoint;
					Vector3D gyrAng = GetNavAngles(MyPosDrill + DrillPoint + new Vector3D(0, 0, 1), DrillMatrix);
					myDriller.gyroBlock.SetOverride(true, gyrAng * ParentProgram.DrillGyroMult, 1);
					myDriller.drillBlock.Turn("Off");

					MaxLSpeed = (float)Math.Sqrt(2 * Math.Abs(MyPosDrill.GetDim(0)) * myDriller.thrustBlock.XMaxA) / 2;
					MaxFSpeed = (float)Math.Sqrt(2 * Math.Abs(MyPosDrill.GetDim(2)) * myDriller.thrustBlock.ZMaxA) / 2;


					if (LeftVelocityVector.Length() < MaxLSpeed)
						myDriller.thrustBlock.SetOverrideAccel("R", (float)(MyPosDrill.GetDim(0) * 1));
					else
					{
						myDriller.thrustBlock.SetOverridePercent("R", 0);
						myDriller.thrustBlock.SetOverridePercent("L", 0);
					}
					if (ForwVelocityVector.Length() < MaxFSpeed)
						myDriller.thrustBlock.SetOverrideAccel("B", (float)(MyPosDrill.GetDim(2) * 1));
					else
					{
						myDriller.thrustBlock.SetOverridePercent("F", 0);
						myDriller.thrustBlock.SetOverridePercent("B", 0);
					}

					if ((UpVelocityVector.Length() < ParentProgram.DrillSpeedLimit * 5) && (MyPosDrill.GetDim(0) < 0.5) && (MyPosDrill.GetDim(2) < 0.5))
						myDriller.thrustBlock.SetOverrideAccel("U", (ParentProgram.DrillAccel * 2));
					else
					{
						myDriller.thrustBlock.SetOverridePercent("U", 0);
					}
					if (MyPosDrill.GetDim(1) > 0)
						Complete = true;
					string strStatus = " STATUS\n";
					if (myDriller.Command == Commands.DepoDrill)
						strStatus += "Cycle: Ore field mining\n";
					else if (myDriller.Command == Commands.RockDrill)
						strStatus += "Cycle: Rock scavenging\n";
					strStatus += "Task: Pull Out\n";
					strStatus += "Cargo Mass: " + myDriller.cargoBlock.CurrentMass.ToString() + "\n";
					strStatus += "Battery charge: " + Math.Round(myDriller.batteryBlock.StoredPower * 100 / myDriller.batteryBlock.MaxPower, 2).ToString() + " % \n";
					strStatus += "Depth: " + Math.Round(-MyPosDrill.GetDim(1), 2).ToString() + " / " + Math.Round(((myDriller.Command == Commands.DepoDrill) ? (ParentProgram.DrillDepth) : 16), 2).ToString() + "\n";
					strStatus += "XZ shifts: " + Math.Round(MyPosDrill.GetDim(0), 2).ToString() + " / " + Math.Round(MyPosDrill.GetDim(2), 2).ToString() + "\n";
					strStatus += "Shafts drilled: " + ShaftN.ToString() + " / " + ParentProgram.MaxShafts.ToString() + "\n";
					myDriller.TextOutput("TP_Status", strStatus);

					return Complete;
				}

				private Vector3D GetSpiralXY(int p, float W, float L, int n = 20)
				{
					int positionX = 0, positionY = 0, direction = 0, stepsCount = 1, stepPosition = 0, stepChange = 0;
					int X = 0;
					int Y = 0;
					for (int i = 0; i < n * n; i++)
					{
						if (i == p)
						{
							X = positionX;
							Y = positionY;
							break;
						}
						if (stepPosition < stepsCount)
						{
							stepPosition++;
						}
						else
						{
							stepPosition = 1;
							if (stepChange == 1)
							{
								stepsCount++;
							}
							stepChange = (stepChange + 1) % 2;
							direction = (direction + 1) % 4;
						}
						if (direction == 0) { positionY++; }
						else if (direction == 1) { positionX--; }
						else if (direction == 2) { positionY--; }
						else if (direction == 3) { positionX++; }
					}
					return new Vector3D(X * W, 0, Y * L);
				}
			}
			private class MyCargo
			{
				public int MaxVolume { get; private set; }
				public int CriticalMass { get; private set; }
				public int CurrentVolume { get; private set; }
				public int CurrentMass { get; private set; }
				public int CurrentOreMass { get; private set; }
				public int FeAmount { get; private set; }
				public int CbAmount { get; private set; }
				public int NiAmount { get; private set; }
				public int MgAmount { get; private set; }
				public int AuAmount { get; private set; }
				public int AgAmount { get; private set; }
				public int PtAmount { get; private set; }
				public int SiAmount { get; private set; }
				public int UAmount { get; private set; }
				public int StoneAmount { get; private set; }
				public int IceAmount { get; private set; }
				public bool StoneDumpNeeded { get; private set; }
				public bool CriticalMassReached { get; private set; }

				private MyDriller myDriller;
				private List<IMyTerminalBlock> CargoGroup = new List<IMyTerminalBlock>();

				public MyCargo(MyDriller mdr)
				{
					myDriller = mdr;
					CriticalMass = (int)(ParentProgram.CriticalMass);
					List<IMyTerminalBlock> TempGroup = new List<IMyTerminalBlock>();
					ParentProgram.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(TempGroup);
					for (int i = 0; i < TempGroup.Count; i++)
					{
						if (TempGroup[i].CustomName.StartsWith(myDriller.MyName))
						{
							IMyTerminalBlock CargoOwner = TempGroup[i] as IMyTerminalBlock;
							if ((CargoOwner != null))
								if ((CargoOwner is IMyShipDrill) || (CargoOwner is IMyCargoContainer) || (CargoOwner is IMyShipConnector))
								{
									MaxVolume += (int)CargoOwner.GetInventory(0).MaxVolume;
									CurrentVolume += (int)(CargoOwner.GetInventory(0).CurrentVolume * 1000);
									CurrentMass += (int)CargoOwner.GetInventory(0).CurrentMass;
									CargoGroup.Add(CargoOwner);
								}
						}
					}
				}

				private void OutputCargoList()
				{
					string Output = " CARGO: " + ((int)(myDriller.thrustBlock.TotalMass * 100 / CriticalMass)).ToString() + "%";
					Output += "\n CurrentMass: " + CurrentMass.ToString();
					//Output+="\n Cargo Mass: "+Math.Round(CargoMass,1);            
					if (FeAmount > 0) { Output += "\n Fe: " + FeAmount; }
					if (CbAmount > 0) { Output += "\n Cb: " + CbAmount; }
					if (NiAmount > 0) { Output += "\n Ni: " + NiAmount; }
					if (MgAmount > 0) { Output += "\n Mg: " + MgAmount; }
					if (AuAmount > 0) { Output += "\n Au: " + AuAmount; }
					if (AgAmount > 0) { Output += "\n Ag: " + AgAmount; }
					if (PtAmount > 0) { Output += "\n Pt: " + PtAmount; }
					if (SiAmount > 0) { Output += "\n Si: " + SiAmount; }
					if (UAmount > 0) { Output += "\n U: " + UAmount; }
					if (IceAmount > 0) { Output += "\n Ice: " + IceAmount; }
					if (StoneAmount > 0) { Output += "\n Stone: " + StoneAmount; }
					Output += "\n Crit Mass:" + CriticalMassReached.ToString();
					Output += "\n Dump Stone:" + StoneDumpNeeded.ToString();
					myDriller.TextOutput("TP_Cargo", Output);
				}
				public void UnLoad()
				{
					var BaseCargo = new List<IMyTerminalBlock>();
					ParentProgram.GridTerminalSystem.SearchBlocksOfName("BaseCargo", BaseCargo);

					for (int ii = 0; ii < BaseCargo.Count; ii++)
					{
						var Destination = BaseCargo[ii].GetInventory(0);
						for (int i = 0; i < CargoGroup.Count; i++)
						{
							var containerInvOwner = CargoGroup[i];
							var containerInv = containerInvOwner.GetInventory(0);
							var containerItems = new List<MyInventoryItem>();
							containerInv.GetItems(containerItems);
							for (int j = 0; j < containerItems.Count; j++)
							{
								containerInv.TransferItemTo(Destination, 0, null, true, null);
							}
						}
					}
					Update();
				}

				public void Update()
				{
					CurrentVolume = 0;
					CurrentMass = 0;
					FeAmount = 0;
					CbAmount = 0;
					NiAmount = 0;
					MgAmount = 0;
					AuAmount = 0;
					AgAmount = 0;
					PtAmount = 0;
					SiAmount = 0;
					UAmount = 0;
					StoneAmount = 0;
					IceAmount = 0;
					for (int i = 0; i < CargoGroup.Count; i++)
					{
						IMyTerminalBlock CargoOwner = CargoGroup[i];
						if (CargoOwner != null)
						{
							CurrentVolume += (int)CargoOwner.GetInventory(0).CurrentVolume;
							CurrentMass += (int)CargoOwner.GetInventory(0).CurrentMass;

							var crateItems = new List<MyInventoryItem>();
							CargoOwner.GetInventory(0).GetItems(crateItems);
							//var crateItems = CargoOwner.GetInventory(0).GetItems();  

							for (int j = crateItems.Count - 1; j >= 0; j--)
							{
								if (crateItems[j].Type.SubtypeId == "Iron")
									FeAmount += (int)crateItems[j].Amount;
								else if (crateItems[j].Type.SubtypeId == "Cobalt")
									CbAmount += (int)crateItems[j].Amount;
								else if (crateItems[j].Type.SubtypeId == "Nickel")
									NiAmount += (int)crateItems[j].Amount;
								else if (crateItems[j].Type.SubtypeId == "Magnesium")
									MgAmount += (int)crateItems[j].Amount;
								else if (crateItems[j].Type.SubtypeId == "Gold")
									AuAmount += (int)crateItems[j].Amount;
								else if (crateItems[j].Type.SubtypeId == "Silver")
									AgAmount += (int)crateItems[j].Amount;
								else if (crateItems[j].Type.SubtypeId == "Platinum")
									PtAmount += (int)crateItems[j].Amount;
								else if (crateItems[j].Type.SubtypeId == "Silicon")
									SiAmount += (int)crateItems[j].Amount;
								else if (crateItems[j].Type.SubtypeId == "Uranium")
									UAmount += (int)crateItems[j].Amount;
								else if (crateItems[j].Type.SubtypeId == "Stone")
									StoneAmount += (int)crateItems[j].Amount;
								else if (crateItems[j].Type.SubtypeId == "Ice")
									IceAmount += (int)crateItems[j].Amount;
							}
						}
					}
					if (myDriller.thrustBlock.TotalMass > CriticalMass)
					{
						CriticalMassReached = true;
					}
					else
					{

						CriticalMassReached = false;
						if (StoneAmount > ParentProgram.StoneDumpOn)
							StoneDumpNeeded = true;
						if (StoneAmount < 100)
							StoneDumpNeeded = false;

					}
					OutputCargoList();
				}
			}

			private class MyBatteries
			{
				public List<IMyTerminalBlock> BatteryGroup = new List<IMyTerminalBlock>();
				public bool LowPower { get; private set; }
				public bool IsCharging { get; private set; }
				public float MaxPower { get; private set; }
				public float StoredPower { get; private set; }
				public float InitialPower { get; private set; }
				public float MinPower { get; private set; }

				private MyDriller myDriller;
				public MyBatteries(MyDriller mdr)
				{
					myDriller = mdr;
					List<IMyTerminalBlock> TempGroup = new List<IMyTerminalBlock>();
					BatteryGroup.Clear();
					ParentProgram.GridTerminalSystem.GetBlocksOfType<IMyBatteryBlock>(TempGroup);
					MaxPower = 0;
					MinPower = ParentProgram.ReturnOnCharge;
					for (int i = 0; i < TempGroup.Count; i++)
					{
						if (TempGroup[i].CustomName.StartsWith(myDriller.MyName))
						{
							IMyBatteryBlock battery = TempGroup[i] as IMyBatteryBlock;
							if (battery != null)
							{
								BatteryGroup.Add(battery);
								MaxPower += battery.MaxStoredPower;
							}
						}
					}
				}
				public void Recharge(bool bRecharge)
				{
					for (int i = 0; i < BatteryGroup.Count; i++)
					{
						IMyBatteryBlock battery = BatteryGroup[i] as IMyBatteryBlock;
						if (battery != null)
							battery.SetValueBool("Recharge", bRecharge);
					}
					IsCharging = bRecharge;
				}
				public void Update()
				{
					StoredPower = 0;
					for (int i = 0; i < BatteryGroup.Count; i++)
					{
						IMyBatteryBlock battery = BatteryGroup[i] as IMyBatteryBlock;
						if (battery != null)
						{
							StoredPower += battery.CurrentStoredPower;
						}
					}
					LowPower = (StoredPower / MaxPower) < MinPower;
				}
			}
			private class MyDrills
			{
				public List<IMyTerminalBlock> DrillGroup = new List<IMyTerminalBlock>();
				public bool DrillSOn { get; private set; }
				public float Width { get; private set; }
				public float Length { get; private set; }
				private MyDriller myDriller;
				public MyDrills(MyDriller mdr)
				{
					myDriller = mdr;
					DrillGroup.Clear();
					List<IMyTerminalBlock> TempGroup = new List<IMyTerminalBlock>();
					ParentProgram.GridTerminalSystem.GetBlocksOfType<IMyShipDrill>(TempGroup);
					for (int i = 0; i < TempGroup.Count; i++)
					{
						if (TempGroup[i].CustomName.StartsWith(myDriller.MyName))
						{
							IMyShipDrill drill = TempGroup[i] as IMyShipDrill;
							if (drill != null)
								DrillGroup.Add(drill);
						}
					}
					Width = ParentProgram.DrillFrameWidth;
					Length = ParentProgram.DrillFrameLength;

				}
				public void Turn(string OnOff)
				{
					for (int i = 0; i < DrillGroup.Count; i++)
					{
						IMyShipDrill drill = DrillGroup[i] as IMyShipDrill;
						if (drill != null)
							drill.GetActionWithName("OnOff_" + OnOff).Apply(drill);
						else
							ParentProgram.GridTerminalSystem.GetBlocksOfType<IMyShipDrill>(DrillGroup);
					}
					if (OnOff == "On")
						DrillSOn = true;
					else
						DrillSOn = false;
				}
			}
			public class MyConnector
			{
				private MyDriller myDriller;
				public IMyShipConnector Connector { get; private set; }
				public bool On { get; private set; }
				public bool Locked { get; private set; }
				public bool ReadyToLock { get; private set; }

				public MyConnector(MyDriller mdr)
				{
					myDriller = mdr;
					Connector = ParentProgram.GridTerminalSystem.GetBlockWithName(myDriller.MyName + "Connector") as IMyShipConnector;
					Locked = (Connector.Status == MyShipConnectorStatus.Connected);
				}

				public void Update()
				{
					if (Connector == null)
						Connector = ParentProgram.GridTerminalSystem.GetBlockWithName(myDriller.MyName + "Connector") as IMyShipConnector;
					else
					{
						Locked = (Connector.Status == MyShipConnectorStatus.Connected);
					}
				}
				public void Turn(string OnOff)
				{
					if (Connector != null)
					{
						Connector.GetActionWithName("OnOff_" + OnOff).Apply(Connector);
						if (OnOff == "On")
							On = true;
						else
							On = false;
					}
					else
					{
						//Connector crashed event!!!            
					}
				}

				public bool Lock()
				{
					Connector.GetActionWithName("OnOff_On").Apply(Connector);
					Connector.GetActionWithName("Lock").Apply(Connector);
					Locked = (Connector.Status == MyShipConnectorStatus.Connected);
					return Locked;
				}

				public bool UnLock()
				{
					Connector.GetActionWithName("Unlock").Apply(Connector);
					Connector.GetActionWithName("OnOff_Off").Apply(Connector);
					Locked = (Connector.Status == MyShipConnectorStatus.Connected);
					return !Locked;
				}
			}

		}

	}
}
