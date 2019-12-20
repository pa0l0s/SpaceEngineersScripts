//using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRageMath;
using VRage.Game.ModAPI.Ingame;

namespace ScriptingClass
{
	public partial class Program : MyGridProgram
	{
		public string referenceBlockName = "cockpit"; //Name of cockpit for ship's orientation reference 
		public string statusLCDName = "Status"; //Name of the LCD that will show Status messages. Not necessary

		//Minimum health a block can have before turning on welders.
		//You could think that better health is just 1, so any damage will turn on welders,
		//but sometimes welders don't totally repair blocks and leave them
		//at like 0.98-0.99 health.
		//If that happens and you have a minimum of 1, then that welder
		//will never turn off again...
		public double minimumBlockHealth = 0.98;

		//Don't modify anything below 
		//=============================================================== 
		public DamagedBlocks dBlocks;
		public StatusMsg status;

		//Numbers of welders in range of each damaged block to activate.
		//You can set it via argument "welders"+N, where N is any number WITHOUT decimals obviously
		//Examples:
		//      welders1 -> only activate the nearest welder
		//      welders3 -> activate the first 3 nearest welders
		//-
		//Before doing it take in mind:
		//1. More welders means worst performance, not only due to welders, but due to the script too.
		//2. More welders means faster repairing.
		//3. If you are not sure, just leave it as it is.
		//  Exact result will depend largely on ship's design, number of blocks and number of welders.
		//  So, i recommend try/error with each ship before go to battle.
		public int weldersToActivate = 2;

		void Main(string argument)
		{
			if (dBlocks == null) dBlocks = new DamagedBlocks(this, (IMyCockpit)GridTerminalSystem.GetBlockWithName(referenceBlockName));
			if (status == null) status = new StatusMsg(this);

			if (argument == "reset")
			{
				status.ClearMessages();
				dBlocks.Reset();
				status.DisplayAll();
			}
			else if (argument.Contains("welders"))
			{
				int index = argument.IndexOf('s');
				string number = argument.Substring(index + 1, argument.Length - index - 1);

				if (!int.TryParse(number, out weldersToActivate))
					status.WriteStatusHeader_Echo("Incorrect number of welders." + "\n" +
						"Format to modify number of welders:" + "\n" +
						"weldersN -> where N is any number WITHOUT decimal.");
				else
				{
					status.ClearMessages();
					status.WriteStatusHeader_Echo("Number of welders to activate changed to " + weldersToActivate.ToString());
					dBlocks.UpdateNumberOfWelders();
					dBlocks.Reset();
					status.DisplayAll();
				}
			}
			else
			{
				dBlocks.AddNewDamagedBlocks();
				dBlocks.RemoveRepairedBlocks();
				status.DisplayAll();
			}
		}

		//Dictionary with more than one value per key,
		//actually it's a dictionary with a list as value.
		public class MyDictionary
		{
			private Dictionary<Vector3D, List<IMyTerminalBlock>> dict;
			private List<Vector3D> lKeys;
			public int maxValuesPerList;

			private Program GridProgram;

			public MyDictionary(Program p, ref int max)
			{
				GridProgram = p;
				dict = new Dictionary<Vector3D, List<IMyTerminalBlock>>();
				lKeys = new List<Vector3D>();
				maxValuesPerList = max;
			}

			public IMyTerminalBlock this[Vector3D key, int i]
			{
				get { return dict[key][i]; }
				set
				{
					try
					{
						dict[key][i] = value;
					}
					catch (KeyNotFoundException)
					{
						this.GridProgram.Echo("MyDictionary KeyNotFoundException when trying to set value for key,index [" +
							key.ToString() + ", " + i.ToString() + "]");
					}
				}
			}

			public List<IMyTerminalBlock> this[Vector3D key]
			{
				get { return dict[key]; }
				set
				{
					try
					{
						dict[key] = value;
					}
					catch (KeyNotFoundException)
					{
						this.GridProgram.Echo("MyDictionary KeyNotFoundException when trying to set list for key [" +
							key.ToString() + "]");
					}
				}
			}

			public bool Add(Vector3D key, IMyTerminalBlock value)
			{
				if (dict.ContainsKey(key))
				{
					if (dict[key].Count < maxValuesPerList)
					{
						dict[key].Add(value);
						return true;
					}
					else return false;
				}
				else
				{
					dict.Add(key, new List<IMyTerminalBlock>());
					dict[key].Add(value);
					lKeys.Add(key);
					return true;
				}
			}

			public bool Add(Vector3D key, List<IMyTerminalBlock> list)
			{
				if (dict.ContainsKey(key)) return false;
				else
				{
					dict.Add(key, list);
					lKeys.Add(key);
					return true;
				}
			}

			public void Remove(Vector3D key)
			{
				if (!dict.ContainsKey(key))
				{
					this.GridProgram.Echo("MyDictionary KeyNotFoundException when trying to Remove");
					return;
				}

				dict.Remove(key);
				lKeys.RemoveAll(i => i == key);
			}

			public void Remove(Vector3D key, int i)
			{
				if (!dict.ContainsKey(key))
				{
					this.GridProgram.Echo("MyDictionary KeyNotFoundException when trying to Remove");
					return;
				}

				dict[key].RemoveAt(i);
			}

			public void Remove(IMyTerminalBlock block)
			{
				lKeys.ForEach(key =>
				{
					if (dict[key].Contains(block))
						dict[key].RemoveAll(b => b == block);
				});
			}

			public bool ContainsKey(Vector3D key) { return dict.ContainsKey(key); }

			public bool ContainsValue(IMyTerminalBlock value)
			{
				for (int i = 0; i < lKeys.Count; i++)
					if (dict[lKeys[i]].Contains(value)) return true;

				return false;
			}

			public void Clear()
			{
				dict.Clear();
				lKeys.Clear();
			}

			public void ForEach(Action<Vector3D> action)
			{
				lKeys.ForEach(key =>
				{
					if (dict.ContainsKey(key))
						action(key);
				});
			}

			public void ForEachInList(Vector3D key, Action<IMyTerminalBlock> action)
			{
				if (!dict.ContainsKey(key))
				{
					this.GridProgram.Echo("MyDictionary KeyNotFoundException when trying to ForEachInList");
					return;
				}

				dict[key].ForEach(block =>
				{
					try { action(block); }
					catch (Exception)
					{
						this.GridProgram.status.WriteStatus_Echo(key, "Block not found.");
					}
				});
			}
		}

		//LCD status
		public class StatusMsg
		{
			private IMyTextPanel Status;
			private Dictionary<Vector3D, string> lMessages;
			private List<Vector3D> lMKeys;
			private string header;

			private Program GridProgram;

			public StatusMsg(Program p)
			{
				GridProgram = p;
				Status = (IMyTextPanel)this.GridProgram.GridTerminalSystem.GetBlockWithName(this.GridProgram.statusLCDName);
				if (Status == null) this.GridProgram.Echo("No Status LCD found");
				else
				{
					lMessages = new Dictionary<Vector3D, string>();
					lMKeys = new List<Vector3D>();
					Status.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
				}
			}

			public void DisplayAll()
			{
				if (Status == null) return;

				Status.WriteText(header);

				lMKeys.ForEach(key =>
				{
					Status.WriteText(lMessages[key], true);
				});
			}

			public void WriteStatusHeader(string str)
			{
				if (Status == null) return;

				header = str + "\n" + "     ==========     " + "\n";
			}

			public void WriteStatusHeader_Echo(string str)
			{
				if (Status == null) return;

				header = str + "\n" + "     ==========     " + "\n";
				this.GridProgram.Echo(str);
			}

			public void WriteStatus(Vector3D key, string str, bool append = false)
			{
				if (Status == null) return;

				if (!append)
				{
					lMessages.Clear();
					lMKeys.Clear();
				}

				if (!lMessages.ContainsKey(key))
				{
					lMessages.Add(key, "\n" + str);
					lMKeys.Add(key);
				}
				else lMessages[key] = str;
			}

			public void WriteStatus_Echo(Vector3D key, string str, bool append = false)
			{
				this.GridProgram.Echo(str);
				WriteStatus(key, str, append);
			}

			public void AddMessageTo(Vector3D key, string str)
			{
				if (!lMessages.ContainsKey(key))
				{
					this.GridProgram.Echo("STATUS: Not found status message to add string");
					return;
				}
				else lMessages[key] += "\n" + str;
			}

			public void AddMessageTo_Echo(Vector3D key, string str)
			{
				this.GridProgram.Echo(str);
				AddMessageTo(key, str);
			}

			public void RemoveStatus(Vector3D key)
			{
				if (Status == null) return;

				lMessages.Remove(key);

				for (int i = 0; i < lMKeys.Count; i++)
				{
					if (lMKeys[i] == key)
					{
						lMKeys.RemoveAt(i);
						break;
					}
				}
			}

			public void ClearMessages()
			{
				lMessages.Clear();
				lMKeys.Clear();
			}
		}

		//Manage damaged blocks 
		public class DamagedBlocks
		{
			private Dictionary<Vector3D, IMyTerminalBlock> dDamagedBlocks;
			private MyDictionary dRepairingWelders;
			private Welders welds;
			private Program GridProgram;

			public DamagedBlocks(Program p, IMyCockpit cp)
			{
				p.Echo("DamagedBlocks creator");

				this.GridProgram = p;
				welds = new Welders(p, cp);
				dDamagedBlocks = new Dictionary<Vector3D, IMyTerminalBlock>();
				dRepairingWelders = new MyDictionary(p, ref this.GridProgram.weldersToActivate);
			}

			public void Reset()
			{
				this.GridProgram.Echo("ResetDamagedBlocks");

				welds.ResetWelders();
				dDamagedBlocks.Clear();
				dRepairingWelders.Clear();
				this.GridProgram.status.WriteStatusHeader_Echo("Damaged blocks reseted");
				this.GridProgram.status.ClearMessages();
			}

			public void UpdateNumberOfWelders()
			{
				dRepairingWelders.maxValuesPerList = this.GridProgram.weldersToActivate;
			}

			public void AddNewDamagedBlocks()
			{
				this.GridProgram.Echo("AddNewDamagedBlocks");
				this.GridProgram.status.WriteStatusHeader("Searching for damaged blocks...");

				List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
				this.GridProgram.GridTerminalSystem.GetBlocks(blocks);
				blocks.ForEach(block =>
				{
					if (GetMyTerminalBlockHealth(ref block) < this.GridProgram.minimumBlockHealth
						&& !dDamagedBlocks.ContainsValue(block)
						&& block != null)
					{
						Vector3D vBlock = welds.GetShipCoordinates(block);
						this.GridProgram.status.WriteStatus_Echo(vBlock, "New damaged block found: " + block.CustomName, true);

						List<IMyTerminalBlock> nearestWelders = welds.FindNearestWeldersTo(block);

						if (nearestWelders != null)
						{
							if (!dDamagedBlocks.ContainsKey(vBlock))
							{
								dDamagedBlocks.Add(vBlock, block);
								dRepairingWelders.Add(vBlock, nearestWelders);
							}

							this.GridProgram.status.AddMessageTo_Echo(vBlock, "Repairing welder found and working");
							dRepairingWelders.ForEachInList(vBlock, b => b.ApplyAction("OnOff_On"));
						}
					}
					else if (block == null)
					{
						this.GridProgram.status.WriteStatusHeader("Removing from dictionaries destroyed or hacked block");
						dDamagedBlocks.Remove(welds.GetShipCoordinates(block));
						dRepairingWelders.Remove(block);
					}
				});
			}

			public void RemoveRepairedBlocks()
			{
				this.GridProgram.Echo("RemoveRepairedBlocks");

				IMyTerminalBlock damBlock;
				dRepairingWelders.ForEach(key =>
				{
					damBlock = dDamagedBlocks[key];

					if (GetMyTerminalBlockHealth(ref damBlock) >= this.GridProgram.minimumBlockHealth && dDamagedBlocks.ContainsKey(key))
					{
						this.GridProgram.Echo("Block " + damBlock.CustomName
							+ " repaired. Turning off welder");

						this.GridProgram.status.RemoveStatus(key);

						dRepairingWelders.ForEachInList(key, block => block.ApplyAction("OnOff_Off"));
						dDamagedBlocks.Remove(key);
						dRepairingWelders.Remove(key);
					}
				});
			}

			//Dr. Novikov snippet: http://forums.keenswh.com/threads/snippet-get-block-health.7368130/ 
			public float GetMyTerminalBlockHealth(ref IMyTerminalBlock block)
			{
				IMySlimBlock slimblock = block.CubeGrid.GetCubeBlock(block.Position);
				float MaxIntegrity = slimblock.MaxIntegrity;
				float BuildIntegrity = slimblock.BuildIntegrity;
				float CurrentDamage = slimblock.CurrentDamage;

				return (BuildIntegrity - CurrentDamage) / MaxIntegrity;
			}
			//--------------- 
		}

		//Manage repairing welders 
		public class Welders
		{
			private Dictionary<Vector3D, IMyTerminalBlock> dWelders;
			private List<Vector3D> lWKeys;
			private Program GridProgram;
			private GridToWorldCoordinates g2w;
			private IMyCockpit cockpit;

			public Welders(Program p, IMyCockpit cp)
			{
				GridProgram = p;
				cockpit = cp;

				g2w = new GridToWorldCoordinates(p.GridTerminalSystem, cockpit.CubeGrid);
				dWelders = new Dictionary<Vector3D, IMyTerminalBlock>();
				lWKeys = new List<Vector3D>();

				this.ResetWelders();
			}

			public void ResetWelders()
			{
				this.GridProgram.Echo("ResetWelders");

				List<IMyTerminalBlock> allWelders = new List<IMyTerminalBlock>();
				this.GridProgram.GridTerminalSystem.GetBlocksOfType<IMyShipWelder>(allWelders);

				dWelders.Clear();
				lWKeys.Clear();

				allWelders.ForEach(block =>
				{
					Vector3D coords = GetShipCoordinates(block);
					dWelders.Add(coords, block);
					lWKeys.Add(coords);
				});
			}

			//Find welder 
			public List<IMyTerminalBlock> FindNearestWeldersTo(IMyTerminalBlock block)
			{
				this.GridProgram.Echo("FindNearestWelderTo " + block.CustomName);

				Vector3D vBlock = GetShipCoordinates(block);
				List<IMyTerminalBlock> retWelder = new List<IMyTerminalBlock>(this.GridProgram.weldersToActivate);
				List<double> distances = new List<double>(this.GridProgram.weldersToActivate);
				FillLists(ref retWelder, ref distances, ref vBlock);

				double distance;

				lWKeys.ForEach(key =>
				{
					distance = Vector3D.Distance(vBlock, key);

					for (int i = 0; i < distances.Count; i++)
					{
						if (distance < distances[i])
						{
							for (int j = retWelder.Count - 1; j > i; j--)
							{
								retWelder[j] = retWelder[j - 1];
								distances[j] = distances[j - 1];
							}

							retWelder[i] = dWelders[key];
							distances[i] = distance;
							break;
						}
					}
				});

				return retWelder;
			}

			private void FillLists(ref List<IMyTerminalBlock> list, ref List<double> listD, ref Vector3D vBlock)
			{
				for (int i = 0; i < this.GridProgram.weldersToActivate; i++)
				{
					list.Add(dWelders[lWKeys[i]]);
					listD.Add(Vector3D.Distance(lWKeys[i], vBlock));
				}
			}

			public Vector3D GetShipCoordinates(IMyTerminalBlock block)
			{
				MatrixD cockpitOrient = MatrixD.Invert(MatrixD.CreateFromDir(g2w.getForward(cockpit), g2w.getUp(cockpit)));

				return Vector3D.TransformNormal(block.GetPosition() - cockpit.GetPosition(), cockpitOrient);
			}
		}

		/////////////////////////////////////////////////////////////////////////////// 
		//Name: Grid To World Coordinates Lib 
		//Version: 2 
		//Changes from v1: Changed interface to use all doubles for better precision 
		//Author: Joe Barker 
		//License: Public Domain 
		/// <summary> 
		/// This class finds the orientation/offset of a grid in world space. 
		/// </summary> 
		public class GridToWorldCoordinates
		{
			///<summary>Matrix transforming from grid-space to world-space</summary> 
			private MatrixD trans;

			///<summary>Matrix transforming from world-space to grid-space</summary> 
			private MatrixD invTrans;

			///<summary>3 selected blocks to use as reference points</summary> 
			private IMyCubeBlock[] refPts;

			///<summary>Local half of the rotation matrix, precalculated because it doesn't change.</summary> 
			private MatrixD refBasis;

			///<summary>Size (in meters) of a cube on this grid.</summary> 
			private double cubeSize;

			///<summary>Build a coordinate system for the provided grid</summary> 
			public GridToWorldCoordinates(IMyGridTerminalSystem GridTerminalSystem, IMyCubeGrid grid)
			{
				trans = new MatrixD(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);//Matrix.Identity; 
				invTrans = trans;
				refBasis = trans;

				cubeSize = grid.GridSize;

				//Find all blocks on the target grid 
				var l = new List<IMyTerminalBlock>();
				GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(l, delegate (IMyTerminalBlock blk)
				{
					return blk.CubeGrid == grid;
				});
				refPts = refFromList(l);
				if (refPts == null) return;

				generateSolver();
				update();
			}

			///<summary>True if valid conversion found, false if no 3 non-collinear reference points found</summary> 
			public bool isValid()
			{ return refPts != null; }

			///<summary>Update conversion for the new position,orientation of the grid.</summary> 
			public void update()
			{
				//Find an orthonormal basis from the reference points 
				//in _world_ coordinates 
				Vector3D q1 = refPts[0].GetPosition();
				Vector3D q2 = refPts[1].GetPosition();
				Vector3D q3 = refPts[2].GetPosition();

				//Work from directions 
				Vector3D u1 = Vector3D.Normalize(q2 - q1);
				Vector3D u2 = Vector3D.Normalize(q3 - q1);

				//Orthoganalize and normalize 
				//Vector3D uo1=u1; 
				Vector3D uo3 = Vector3D.Normalize(u1.Cross(u2));
				Vector3D uo2 = Vector3D.Normalize(uo3.Cross(u1));

				trans = new MatrixD(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
				trans.SetDirectionVector(Base6Directions.Direction.Right, u1);
				trans.SetDirectionVector(Base6Directions.Direction.Up, uo2);
				trans.SetDirectionVector(Base6Directions.Direction.Backward, uo3);

				//Rotate from grid coord to world coord 
				trans = MatrixD.Transpose(refBasis) * trans;
				invTrans = MatrixD.Transpose(trans);

				//Find origin 
				Vector3D p1 = center(refPts[0]) * cubeSize;
				Vector3D origin = q1 - Vector3D.Transform(p1, trans);
				trans = trans * MatrixD.CreateTranslation(origin.GetDim(0), origin.GetDim(1), origin.GetDim(2));
				invTrans = MatrixD.CreateTranslation(-origin.GetDim(0), -origin.GetDim(1), -origin.GetDim(2)) * invTrans;
			}

			///<summary>Transform grid-coordinates to world-coordinates</summary> 
			public Vector3D transform(Vector3D gridCoord)
			{ return Vector3D.Transform(cubeSize * gridCoord, ref trans); }

			///<summary>Transform world-coordinates to grid-coordinates</summary> 
			public Vector3D reverse(Vector3D worldCoord)
			{ return Vector3D.Transform(worldCoord, ref invTrans) / cubeSize; }

			///<summary>Transform direction in grid-space to world-space</summary> 
			public Vector3D transformDir(Vector3D gridDir)
			{ return Vector3D.Transform(gridDir, trans.GetOrientation()); }

			///<summary>Transform direction in world-space to grid-space</summary> 
			public Vector3D reverseDir(Vector3D worldDir)
			{ return Vector3D.Transform(worldDir, invTrans.GetOrientation()); }

			///<summary>World space location for grid-coord (0,0,0)</summary> 
			public Vector3D Origin
			{ get { return Vector3D.Transform(new Vector3D(0, 0, 0), ref trans); } }

			///<summary>Forward (-Z) world-space direction for the grid</summary> 
			public Vector3D Forward
			{ get { return -trans.GetDirectionVector(Base6Directions.Direction.Backward); } }

			///<summary>Backward (+Z) world-space direction for the grid</summary> 
			public Vector3D Backward
			{ get { return trans.GetDirectionVector(Base6Directions.Direction.Backward); } }

			///<summary>Left (-X) world-space direction for the grid</summary> 
			public Vector3D Left
			{ get { return -trans.GetDirectionVector(Base6Directions.Direction.Right); } }

			///<summary>Right (+X) world-space direction for the grid</summary> 
			public Vector3D Right
			{ get { return trans.GetDirectionVector(Base6Directions.Direction.Right); } }

			///<summary>Up (+Y) world-space direction for the grid</summary> 
			public Vector3D Up
			{ get { return trans.GetDirectionVector(Base6Directions.Direction.Up); } }

			///<summary>Down (-Y) world-space direction for the grid</summary> 
			public Vector3D Down
			{ get { return -trans.GetDirectionVector(Base6Directions.Direction.Up); } }

			///<summary>Get the world-space direction for the given block's Forward vector</summary> 
			///<remark>For example, a thruster thrusts in the forward direction and a cockpit faces in the forward direction.</remark> 
			public Vector3D getForward(IMyCubeBlock blk)
			{
				Matrix bmat;
				blk.Orientation.GetMatrix(out bmat);
				return transformDir(bmat.Forward);
			}

			///<summary>Get the world-space direction for the given block's Left vector</summary> 
			public Vector3D getLeft(IMyCubeBlock blk)
			{
				Matrix bmat;
				blk.Orientation.GetMatrix(out bmat);
				return transformDir(bmat.Left);
			}

			///<summary>Get the world-space direction for the given block's Up vector</summary> 
			public Vector3D getUp(IMyCubeBlock blk)
			{   // Orientation.Up doesn't work (returns .Forward), so do this instead 
				Matrix bmat;
				blk.Orientation.GetMatrix(out bmat);
				return transformDir(bmat.Forward.Cross(bmat.Left));
			}

			///<summary>Return the grid-space center of the given block.</summary> 
			///<remark>blk.Position isn't always the exact center of the block, this is.</remark> 
			public static Vector3D center(IMyCubeBlock blk)
			{ return (new Vector3D(blk.Min + blk.Max)) / 2.0f; }

			/////////////////// 

			private void generateSolver()
			{
				//Find an orthonormal basis from the reference points 
				//in grid coordinates 
				Vector3D p1 = center(refPts[0]);
				Vector3D p2 = center(refPts[1]);
				Vector3D p3 = center(refPts[2]);

				//Work from directions 
				Vector3D v1 = Vector3D.Normalize(p2 - p1);
				Vector3D v2 = Vector3D.Normalize(p3 - p1);

				//Orthogonalize and normalize 
				//Vector3D vo1=v1; 
				Vector3D vo3 = Vector3D.Normalize(v1.Cross(v2));
				Vector3D vo2 = Vector3D.Normalize(vo3.Cross(v1));

				refBasis = new MatrixD(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
				refBasis.SetDirectionVector(Base6Directions.Direction.Right, v1);
				refBasis.SetDirectionVector(Base6Directions.Direction.Up, vo2);
				refBasis.SetDirectionVector(Base6Directions.Direction.Backward, vo3);
			}

			private static IMyCubeBlock[] refFromList(List<IMyTerminalBlock> l)
			{   //Find 3 non-co-linear points 
				for (int i = 0; i < l.Count; ++i)
				{
					Vector3D pi = center(l[i]);
					for (int j = i + 1; j < l.Count; ++j)
					{
						Vector3D pj = center(l[j]);
						Vector3D vij = Vector3D.Normalize(pj - pi);
						for (int k = j + 1; k < l.Count; ++k)
						{
							Vector3D pk = center(l[k]);
							Vector3D vik = Vector3D.Normalize(pk - pi);
							if (Math.Abs(vij.Dot(vik)) < 0.8)
								return new IMyCubeBlock[] { l[i], l[j], l[k] };
						}
					}
				}
				return null;
			}
		}
	}
}
