using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
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
			Runtime.UpdateFrequency = UpdateFrequency.Update100;
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

		//Name of camera block
		String SCAN_CAMERA = "SCAN_CAMERA";

		//Name of lcd panel
		String SCAN_INFO = "SCAN_INFO";

		String SCAN_INFO2 = "SCAN_INFO_2";

		//Trigger block on detection
		String TIMERBLOCKTRIGGER = string.Empty;

		double RANGE = 20000;
		float PITCH = 0;
		float YAW = 0;
		private IMyCameraBlock camera;
		private IMyTextPanel lcd;
		private IMyTextPanel lcd2;
		private IMyTimerBlock trigger;
		private bool firstrun = true;
		private MyDetectedEntityInfo info;
		private StringBuilder sb = new StringBuilder();
		private bool triggerBlockOnContact = false;

		void Main(String setRange)
		{

			if (!string.IsNullOrEmpty(setRange))
			{
				RANGE = Convert.ToDouble(setRange.Trim());
			}
			if (firstrun)
			{
				firstrun = false;
				Echo("Initializing System, detecting requirements.");
				Echo("Searching camera...");
				camera = GridTerminalSystem.GetBlockWithName(SCAN_CAMERA) as IMyCameraBlock;
				if (camera != null)
				{
					Echo($"Camera {camera.CustomName} found.");
				}
				else
				{
					Echo($"Camera {SCAN_CAMERA} not found.");
				}
				Echo("Searching output lcd panel...");
				lcd = GridTerminalSystem.GetBlockWithName(SCAN_INFO) as IMyTextPanel;
				if (lcd != null)
				{
					Echo($"lcd {camera.CustomName} found.");
				}
				else
				{
					Echo($"lcd {SCAN_INFO} not found.");
				}

				lcd2 = GridTerminalSystem.GetBlockWithName(SCAN_INFO2) as IMyTextPanel;
				if (lcd != null)
				{
					Echo($"lcd {camera.CustomName} found.");
				}
				else
				{
					Echo($"lcd {SCAN_INFO} not found.");
				}

				if (TIMERBLOCKTRIGGER != string.Empty)
				{
					//we are to trigger a block on contact
					Echo("Searching camera...");
					trigger = GridTerminalSystem.GetBlockWithName(TIMERBLOCKTRIGGER) as IMyTimerBlock;
					if (trigger != null)
					{
						Echo($"Timer {trigger.CustomName} found.");
						triggerBlockOnContact = true;
					}
					else
					{
						Echo($"Timer {TIMERBLOCKTRIGGER} not found.");
					}

				}
				camera.EnableRaycast = true;
			}

			sb.Clear();
			if (camera.CanScan(RANGE))
			{

				info = camera.Raycast(RANGE, PITCH, YAW);
				sb.Append($"Last scan at range: {RANGE}");
			}
			else
			{


				sb.Append($"Powering up for scan... Current range: {camera.AvailableScanRange}");

			}


			if (info.EntityId == 0)
			{
				//nothing found
				sb.AppendLine();
				sb.Append($"No target found within  {RANGE} m");
				lcd.SetValue("FontColor", Color.White);
				lcd.WritePublicText(sb.ToString());
				lcd.ShowPublicTextOnScreen();
				return;
			}
			if (info.HitPosition.HasValue)
			{
				if (triggerBlockOnContact)
				{
					trigger.Trigger();
				}
				sb.AppendLine();
				sb.Append($"Target found at {(Vector3D.Distance(camera.GetPosition(), info.HitPosition.Value)):0.00}m range");
			}


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

			lcd.SetValue("FontColor", color);
			lcd.WritePublicText(sb.ToString());

			lcd.ShowPublicTextOnScreen();

			lcd2.SetValue("FontColor", color);
			lcd2.WritePublicText(sb.ToString());

			lcd2.ShowPublicTextOnScreen();
		}









	}
}
