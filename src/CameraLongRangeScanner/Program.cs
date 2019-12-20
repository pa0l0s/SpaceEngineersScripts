using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRageMath;

namespace ScriptingClass
{
	public class Program : MyGridProgram
	{

		//void Main()
		//{
		//	string strPBGPS = "GPS:PBGPSLoc" + Me.GetPosition().ToString("0.000").Replace("{", "").Replace("}", "").Replace("X", "").Replace("Y", "").Replace("Z", "").Replace(" ", "") + ":";
		//	Echo(strPBGPS);
		//	Me.CustomData = strPBGPS;
		//}

		PinpointScanSystem system;

		public Program()

		{

			// The constructor, called only once every session and
			// always before any other method is called. Use it to
			// initialize your script. 
			//     
			// The constructor is optional and can be removed if not
			// needed.
			// 
			// It's recommended to set RuntimeInfo.UpdateFrequency 
			// here, which will allow your script to run itself without a 
			// timer block.

			// Configure this program to run the Main method every 100 update ticks
			//Runtime.UpdateFrequency = UpdateFrequency.Update100;

			system = new PinpointScanSystem(this);
		}

		public void Save()

		{

			// Called when the program needs to save its state. Use
			// this method to save your state to the Storage field
			// or some other means. 
			// 
			// This method is optional and can be removed if not
			// needed.

		}

		// Camera Scan script
		// BY DerLaCroix  
		// Version 0.1 (17.12.2016)   
		// Script will scan for targets at given range (20000m default)
		//If different range is wanted (faster scanning, less power), set the Run parameter to distance,
		//and trigger Timer block with settings "Run with default parameter" - it will then always use the
		//range indicated in the Run textbox.
		//NEEDS 
		//"SCAN_CAMERA" Specify Camera name here;  
		//"SCAN_INFO" Specify Lcd name here; 
		//Timer block to run with deault settings
		//Optional:
		//Second Timer block to be triggererd on detection - specify name in variable TIMERBLOCKTRIGGER

		//New wey to setup
		//Add all cammeras and lcds you wont to use to group with name "ScanSystem"

		public void Main(string argument, UpdateType updateSource)

		{

			// The main entry point of the script, invoked every time
			// one of the programmable block's Run actions are invoked,
			// or the script updates itself. The updateSource argument
			// describes where the update came from.
			// 
			// The method itself is required, but the arguments above
			// can be removed if not needed.

			if (string.IsNullOrEmpty(argument))
			{
				system.DoScan();
			}

			system.SetRange(argument);

		}

		public class PinpointScanSystem
		{

			//New wey to setup
			//Add all cammeras and lcds you wont to use to group with name "ScanSystem"

			const string SCAN_SYSTEM_GROUP = "ScanSystem";

			//Trigger block on detection
			String TIMERBLOCKTRIGGER = string.Empty;

			double RANGE = 20000;
			float PITCH = 0;
			float YAW = 0;
			//private IMyCameraBlock camera;
			//private IMyTextPanel lcd;
			//private IMyTextPanel lcd2;
			private IMyTimerBlock trigger;
			//private bool firstrun = true;
			//private MyDetectedEntityInfo info;
			//private StringBuilder sb = new StringBuilder();
			private bool triggerBlockOnContact = false;


			private Program _program;
			private List<IMyCameraBlock> cameras;
			private List<IMyTextPanel> displays;

			List<MyDetectedEntityInfo> detectedList;

			public PinpointScanSystem(Program program)
			{
				_program = program;

				detectedList = new List<MyDetectedEntityInfo>();

				IMyBlockGroup scanSystemGroup = _program.GridTerminalSystem.GetBlockGroupWithName(SCAN_SYSTEM_GROUP);
				if (scanSystemGroup == null)
				{
					throw new Exception($"No {SCAN_SYSTEM_GROUP} group defined.");
				}
				cameras = new List<IMyCameraBlock>();
				scanSystemGroup.GetBlocksOfType<IMyCameraBlock>(cameras);
				if (cameras.Count == 0)
				{
					throw new Exception("No camera found in group.");
				}
				displays = new List<IMyTextPanel>();
				scanSystemGroup.GetBlocksOfType<IMyTextPanel>(displays);
				if (cameras.Count == 0)
				{
					throw new Exception("No text pannel found in group.");
				}

				if (TIMERBLOCKTRIGGER != string.Empty)
				{
					//we are to trigger a block on contact
					_program.Echo("Searching timmer block...");
					trigger = _program.GridTerminalSystem.GetBlockWithName(TIMERBLOCKTRIGGER) as IMyTimerBlock;
					if (trigger != null)
					{
						_program.Echo($"Timer {trigger.CustomName} found.");
						triggerBlockOnContact = true;
					}
					else
					{
						_program.Echo($"Timer {TIMERBLOCKTRIGGER} not found.");
					}

				}

				//Enable raycast for cameras
				foreach (var camera in cameras)
				{
					camera.EnableRaycast = true;
				}

			}

			public void SetRange(string setRangeValue)
			{
				if (!string.IsNullOrEmpty(setRangeValue))
				{
					RANGE = Convert.ToDouble(setRangeValue.Trim());
				}
			}
			public void DoScan()
			{

				_program.Echo("Prepare scan");
				//sb.Clear();



				foreach (var camera in cameras)
				{
					var info = camera.Raycast(RANGE, PITCH, YAW);
					if (!info.IsEmpty())
					{
						if (!detectedList.Exists(x => x.EntityId == info.EntityId))
						{

							DisplayNonEmptyInfo(info, camera);
							detectedList.Add(info);
						}
					}
					DisplayEmptyInfo(info);
				}
			}

			private void DisplayEmptyInfo(MyDetectedEntityInfo info)
			{
				var sb = new StringBuilder();
				sb.Append($"No target found within  {RANGE} m");
				var firstDisplay = displays.OrderBy(x => x.DisplayNameText).FirstOrDefault();
				//firstDisplay.SetValue("FontColor", Color.White);
				//firstDisplay.WritePublicText(sb.ToString());
				//firstDisplay.ShowPublicTextOnScreen();
				return;
			}

			private void DisplayNonEmptyInfo(MyDetectedEntityInfo info, IMyCameraBlock camera)
			{
				var sb = new StringBuilder();

				if (info.HitPosition.HasValue)
				{
					if (triggerBlockOnContact)
					{
						trigger.Trigger();
					}
					sb.AppendLine();
					sb.AppendLine($"Time: {DateTime.Now.ToString()}");
					sb.Append($"Target found at {(Vector3D.Distance(camera.GetPosition(), info.HitPosition.Value)):0.00}m range");

					String size = info.BoundingBox.Size.ToString("0.000");
					String printSize = string.Empty;
					double volume = 1;
					String unitName = "m3";
					if (size != null || size != string.Empty)
					{
						size = size.Replace("{", string.Empty).Replace("}", string.Empty).Replace("X", string.Empty).Replace("Y", string.Empty).Replace("Z", string.Empty).Replace(" ", string.Empty);
						size = size.Trim();
						String[] sizes = size.Split(':');

						double factor = 1;

						if (info.Type.ToString() == "SmallGrid")
						{
							factor = 4;
							unitName = "blocks";
						}
						else if (info.Type.ToString() == "LargeGrid")
						{
							factor = 16;
							unitName = "blocks";
						}
						foreach (String measure in sizes)
						{
							if (measure == string.Empty)
							{
								//first value
								continue;
							}

							volume = volume * Math.Round(Convert.ToDouble(measure), 0);

							printSize += $"{Convert.ToDouble(measure):0.00} X ";
						}
						volume = volume / factor;
					}
					printSize = printSize.Remove(printSize.Length - 2);
					printSize += "m";

					//X:5.302 Y:3.072 Z:4.252

					sb.AppendLine();
					sb.Append($"Found grid ID: {info.EntityId}");
					sb.AppendLine();
					sb.Append($"Name of object: {info.Name}");
					sb.AppendLine();
					sb.Append($"Type of object: {info.Type}");
					sb.AppendLine();
					sb.Append($"Current velocity: {info.Velocity.ToString("0.000")}");
					sb.AppendLine();
					sb.Append($"Relationship: {info.Relationship}");
					sb.AppendLine();
					sb.Append($"Size: {printSize}");
					sb.AppendLine();
					sb.Append($"Estimated volume: {volume} {unitName}");
					sb.AppendLine();
					sb.Append($"Position: {info.Position.ToString("0.000")}");
					sb.AppendLine();
					sb.Append($"GPS:object{(info.Position.ToString("0.000").Replace("{", string.Empty).Replace("}", string.Empty).Replace("X", string.Empty).Replace("Y", string.Empty).Replace("Z", string.Empty).Replace(" ", string.Empty))}:");

					Color color = Color.White;

					if (info.Relationship.ToString() == "Owner")
					{
						color = Color.Green;
					}
					else if (info.Relationship.ToString() == "Friendly")
					{
						color = Color.Green;
					}
					else if (info.Relationship.ToString() == "FactionShare")
					{
						color = Color.Green;
					}
					else if (info.Relationship.ToString() == "Floating")
					{
						color = Color.Yellow;
					}
					else if (info.Relationship.ToString() == "NoOwnership")
					{
						color = Color.Yellow;
					}
					else if (info.Relationship.ToString() == "Enemies")
					{
						color = Color.Red;
					}

					if (info.HitPosition.HasValue)
					{
						sb.AppendLine();
						sb.Append($"Hit point: {info.HitPosition.Value.ToString("0.000")}");
						sb.AppendLine();
						sb.Append($"GPS:objectHit{(info.HitPosition.Value.ToString("0.000").Replace("{", string.Empty).Replace("}", string.Empty).Replace("X", string.Empty).Replace("Y", string.Empty).Replace("Z", string.Empty).Replace(" ", string.Empty))}:");
					}

					DisplayNonEmptyInfo(sb.ToString(), color);
				}
			}

			private void DisplayNonEmptyInfo(string text, Color color)
			{
				var displaysOrdered = displays.OrderBy(x => x.DisplayNameText).ToList();

				for (int i = displaysOrdered.Count - 1; i > 0; i--)
				{

					DisplayNonEmptyInfo(displaysOrdered[i], displaysOrdered[i - 1].GetText(), displaysOrdered[i - 1].GetValueColor("FontColor"));
				}
				DisplayNonEmptyInfo(displaysOrdered[0], text, color);
			}
			private void DisplayNonEmptyInfo(IMyTextPanel display, string text, Color color)
			{
				display.SetValue("FontColor", color);
				display.WriteText(text);
				display.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
				//display.WritePublicText(text);
				//display.ShowPublicTextOnScreen();
			}

		}

		//CUT HERE
	}

}
