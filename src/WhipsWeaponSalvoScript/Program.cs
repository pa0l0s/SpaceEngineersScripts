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
using VRage.Game.ModAPI.Ingame.Utilities;
using System.Linq;

namespace ScriptingClass
{
	public class Program : MyGridProgram
	{
		#region DONT FREAKING TOUCH THESE
		const string VERSION = "43.3.1";
		const string DATE = "03/03/2019";
		#endregion

		/*     
		/ //// / Whip's Multi-Group Weapon Salvo Script / //// /
		PUBLIC RELEASE
		HOWDY!  
		______________________________________________________________________________________    
		SETUP INSTRUCTIONS

			1.) Place this script in a programmable block (No timer is needed!)
			2.) Make a group of your weapons with the name "Salvo Group <unique tag>" where <unique tag> is a unique word or phrase
			3.) You can make as many salvo groups as you want!
		______________________________________________________________________________________        
		ARGUMENTS 

		Type in these arguments without quotes. Arguments are case INSENSITIVE. 
		These arguments can be input manually to the program argument field, 
		this program's Custom Data, through timers, or through sensors. 

		In the Custom Data, command lines can be separated with semi-colons, or they can each have
		their own lines. 

		> BASIC ARGUMENT SYNTAX
			<Group name tag>:<command 1>

		> ADVANCED ARGUMENT SYNTAX
			You can also execute several commands to the same group.
			<Group name tag>:<command 1>:<command 2>

		> MULTI-GROUP COMMAND SYNTAX
			You can also execute several commands to several different groups in one line
			<Group 1 name>:<command 1>:<command 2>;<Group 2 name>:<command 1>:<command 2>

		> AVAILABLE COMMANDS

			RPS <number>   
				Changes the rate of fire in rounds per second.  
				* [Maximum RPS] = [Standard RPS] * [Number of sequenced weapons] 
					NOTE: The script will round this number, this is not a bug! 

			RPM <number>    
				Changes the rate of fire in rounds per minute.  
				* [Maximum RPM] = 60 * [Standard RPM] * [Number of sequenced weapons] 
					NOTE: The script will round this number, this is not a bug! 

			delay <integer>  
				Sets number of ticks between shots (60 ticks = 1 sec)

			burst <integer>
				Shoots a burst with the specified number of shots

			default  
				Lets the script to set the fire rate automatically based on the number of     
				available weapons (using the default fixed rocket rate of fire). The script 
				will attempt to fire ALL sequenced weapons in the span of ONE second with 
				this particular setting.

			fire_on   
				Toggles fire on only  

			fire_off  
				Toggles fire off only  

			fire_toggle 
				Toggles fire on/off  

		______________________________________________________________________________________     
		EXAMPLES: 

		"Salvo Group 1 : fire_on" 
			Toggles the weapons' firing on and use default rate of fire in any group with 
			"Salvo Group 1" in its name

		"Salvo Group 2 : default" 
			Resets the default rate of fire in any group with "Salvo Group 2" in its name

		"Salvo Group 3 : rpm 10 : fire_toggle" 
			Sets the rate of fire to 10 rounds per minute and toggles firing in any group
			with "Salvo Group 3" in its name

		"Salvo Group 1 : rps 10 ; Salvo Group 2 : rps 5" 
			Sets the rate of fire to 10 rounds per second on any any group named "Salvo Group 1" 
			and also sets the rate of fire to 5 rounds per second on any any group named "Salvo Group 2"  

		______________________________________________________________________________________     
		AUTHOR'S NOTES:

		If you have any questions feel free to post them on the workshop page!             

		- Whiplash141
		*/

		//=================================================
		/////////DO NOT TOUCH ANYTHING BELOW HERE/////////
		//=================================================
		string salvoGroupNameTag = "Salvo Group";

		const double refreshTime = 10; //seconds
		double currentRefreshTime = 141;
		bool isSetup = false;
		const double runtimeToRealTime = 1.0 / 0.96;
		const double updateTime = 1.0 / 60.0;
		double currentTime = 141;
		double echoTime = 141;
		readonly StringBuilder salvoGroupSB = new StringBuilder();
		readonly MyIni configIni = new MyIni();

		const string INI_SECTION_TAG = "Weapon Salvo Config";
		const string INI_KEY_NAMETAG = "Salvo group nametag";
		const string INI_KEY_ARGS = "Arguments";
		string userCommands = "";

		RuntimeTracker runtimeTracker;

		Program()
		{
			Runtime.UpdateFrequency = UpdateFrequency.Update1;
			Echo("If this code is not running\nclick the 'Run' button!");
			runtimeTracker = new RuntimeTracker(this, 120);
		}

		void LoadIni()
		{
			configIni.Clear();
			bool parsed = configIni.TryParse(Me.CustomData);

			if (parsed)
			{
				salvoGroupNameTag = configIni.Get(INI_SECTION_TAG, INI_KEY_NAMETAG).ToString(salvoGroupNameTag);
				userCommands = configIni.Get(INI_SECTION_TAG, INI_KEY_ARGS).ToString(userCommands);
			}

			SaveIni();
		}

		void SaveIni()
		{
			configIni.Clear();
			configIni.Set(INI_SECTION_TAG, INI_KEY_NAMETAG, salvoGroupNameTag);
			configIni.Set(INI_SECTION_TAG, INI_KEY_ARGS, userCommands);

			Me.CustomData = configIni.ToString();
		}

		void Main(string arg, UpdateType updateSource)
		{
			runtimeTracker.AddRuntime();

			bool queueArg = false;

			if ((updateSource & (UpdateType.Trigger | UpdateType.Terminal)) != 0)
			{
				queueArg = true;
			}

			if (!isSetup || currentRefreshTime >= refreshTime)
			{
				LoadIni();
				isSetup = GrabBlockGroups();
				currentRefreshTime = 0;
				queueArg = true;
			}

			if (queueArg)
			{
				foreach (var thisGroup in salvoGroups)
				{
					thisGroup.ProcessArgument($"{arg};{userCommands}");
				}
			}

			if (!isSetup)
				return;

			var lastRuntime = runtimeToRealTime * Math.Max(Runtime.TimeSinceLastRun.TotalSeconds, 0);
			currentTime += lastRuntime;
			currentRefreshTime += lastRuntime;
			echoTime += lastRuntime;

			if (currentTime + 1e-6 >= updateTime)
			{
				currentTime = 0;
				salvoGroupSB.Clear();
				salvoGroupSB.AppendLine($"Whip's Weapon Salvo Code\n(Version {VERSION} - {DATE})\n\nNext block refresh in {Math.Max(0, refreshTime - currentRefreshTime):N0} seconds");

				try
				{
					foreach (var thisGroup in salvoGroups)
					{
						thisGroup.SequenceWeapons();
						salvoGroupSB.Append(thisGroup.EchoBuilder);
					}
				}
				catch
				{
					Echo("EXCEPTION OCCURED\nREFRESHING SCRIPT...");
					isSetup = false;
				}

				salvoGroupSB.Append(runtimeTracker.Write());
			}

			if (echoTime > 1)
			{
				Echo(salvoGroupSB.ToString());
				echoTime = 0;
			}

			runtimeTracker.AddInstructions();
		}

		List<WeaponSalvoGroup> salvoGroups = new List<WeaponSalvoGroup>();
		List<IMyBlockGroup> sequenceGroups = new List<IMyBlockGroup>();
		HashSet<IMyBlockGroup> cachedSequenceGroups = new HashSet<IMyBlockGroup>();
		bool GrabBlockGroups()
		{
			sequenceGroups.Clear();
			cachedSequenceGroups.Clear();

			GridTerminalSystem.GetBlockGroups(sequenceGroups, x => x.Name.ToLower().Contains(salvoGroupNameTag.ToLower()));

			if (sequenceGroups.Count == 0)
			{
				Echo($"----------------------------------\nERROR: No groups containing the\n name tag '{salvoGroupNameTag}' were found");
				return false;
			}

			//removes salvo groups that dont exist any more
			salvoGroups.RemoveAll(x => !sequenceGroups.Contains(x.ThisGroup));

			//Update existing salvo groups
			foreach (var group in salvoGroups)
			{
				group.GetBlocks();
			}

			//add groups that currently exist and are already initialized to a list
			foreach (var salvoGroup in salvoGroups)
			{
				cachedSequenceGroups.Add(salvoGroup.ThisGroup);
			}

			//Echo($"cachedSequenceGroups: {cachedSequenceGroups.Count}");

			foreach (var group in sequenceGroups)
			{
				if (!cachedSequenceGroups.Contains(group))
					salvoGroups.Add(new WeaponSalvoGroup(group, this)); //add groups that now exist, but were not initialized
			}
			return true;
		}

		public class WeaponSalvoGroup
		{
			public readonly StringBuilder EchoBuilder = new StringBuilder();
			public IMyBlockGroup ThisGroup { get; private set; } = null;
			Program _thisProgram = null;
			List<IMyUserControllableGun> _weapons = new List<IMyUserControllableGun>();
			int _weaponCount = 0;
			int _timeCount = 0;
			int _delay = 0;
			int _defaultRateOfFire = 1;
			int _burstCount = 0;
			double _desiredRPM = 0;
			bool _isShooting = false;
			bool _executeToggle = false;
			bool _manualOverride = false;
			bool _limitReached = false;
			bool _shouldBurst = false;
			string _messageToggle = "";
			string _messageOverride = "";
			IMyUserControllableGun weaponToFire = null;

			public WeaponSalvoGroup(IMyBlockGroup group, Program program)
			{
				ThisGroup = group;
				_thisProgram = program;
				this.GetBlocks();
			}

			void Echo(string echoStr)
			{
				EchoBuilder.AppendLine(echoStr);
			}

			public void GetBlocks()
			{
				ThisGroup.GetBlocksOfType(_weapons, x => !(x is IMyLargeTurretBase) && x.IsFunctional && _thisProgram.Me.IsSameConstructAs(x));

				//Sorting method
				_weapons.Sort((gun1, gun2) => gun1.CustomName.CompareTo(gun2.CustomName));
			}

			public void ProcessArgument(string argument = "")
			{
				//It's splittin' time!     
				string[] argumentSplit = argument.Split(new char[] { '\n', ';' });  //split at semicolons and new lines

				//====ARGUMENT HANDLING====
				#region ARGUMENT HANDLING
				foreach (string thisArgument in argumentSplit)
				{
					var argumentFields = thisArgument.Split(':');
					if (argumentFields.Length < 2) //no valid command
						continue;

					if (!ThisGroup.Name.Trim().Equals(argumentFields[0].Trim(), StringComparison.OrdinalIgnoreCase)) //explicit name that trims spaces
						continue;
					Echo("TEST");
					for (int i = 1; i < argumentFields.Length; i++)
					{
						string command = argumentFields[i].ToLower().Trim();

						if (command.StartsWith("rps", StringComparison.OrdinalIgnoreCase)) //change rate of fire manually    
						{
							var value = command.Replace("rps", "").Trim();

							double valueDouble;
							bool isDouble = double.TryParse(value, out valueDouble);
							if (!isDouble) continue;

							double delayUnrounded = 60.0 / valueDouble; //Dont change this from 60 
							_desiredRPM = valueDouble * 60.0;
							_delay = (int)Math.Ceiling(delayUnrounded);
							_manualOverride = true;
						}
						if (command.StartsWith("rpm", StringComparison.OrdinalIgnoreCase)) //change rate of fire manually    
						{
							var value = command.Replace("rpm", "").Trim();

							double valueDouble;
							bool isDouble = double.TryParse(value, out valueDouble);
							if (!isDouble) continue;

							double delayUnrounded = 3600.0 / valueDouble;
							_desiredRPM = valueDouble;
							_delay = (int)Math.Ceiling(delayUnrounded);
							_manualOverride = true;
						}
						else if (command.StartsWith("delay", StringComparison.OrdinalIgnoreCase)) //change delay (in ticks) between shots; 60 ticks = 1 sec    
						{
							var value = command.Replace("delay", "").Trim(); //trim spaces and remove keyword

							int valueInteger = 0;
							bool isInteger = int.TryParse(value, out valueInteger);
							if (!isInteger) continue;

							_delay = valueInteger;
							_desiredRPM = 3600.0 / _delay;
							_manualOverride = true;
						}
						else if (command.StartsWith("burst", StringComparison.OrdinalIgnoreCase))
						{
							var value = command.Replace("burst", "").Trim(); //trim spaces and remove keyword

							int valueInteger = 0;
							bool isInteger = int.TryParse(value, out valueInteger);
							if (!isInteger) continue;

							_burstCount = valueInteger;
							_executeToggle = true;
							_shouldBurst = true;
						}
						else if (command.Equals("default", StringComparison.OrdinalIgnoreCase)) //lets the script set fire rate       
						{
							_manualOverride = false;
						}
						else if (command.Equals("fire_on", StringComparison.OrdinalIgnoreCase)) //toggle fire on      
						{
							_executeToggle = true;
						}
						else if (command.Equals("fire_off", StringComparison.OrdinalIgnoreCase)) //toggle fire off
						{
							_executeToggle = false;
						}
						else if (command.Equals("fire_toggle", StringComparison.OrdinalIgnoreCase)) //toggle fire on/off
						{
							_executeToggle = !_executeToggle;
						}
					}

				}
				#endregion
			}

			public void SequenceWeapons()
			{
				//GetBlocks();
				EchoBuilder.Clear();

				if (_weapons.Count == 0)
				{
					Echo("----------------------------------\n ERROR: No weapons in group '" + ThisGroup.Name + "' were found.");
					return;
				}

				if (_weapons[0].CubeGrid.GridSizeEnum == MyCubeSize.Large)
				{
					_defaultRateOfFire = 2;
				}
				else
					_defaultRateOfFire = 1;

				//Checks for divide by zero
				if (_delay == 0)
				{
					_delay = 1; //stops divide by zero 
					_desiredRPM = 3600.0 / _delay;
				}

				//Sets default rate of fire
				if (!_manualOverride)
				{
					var delayUnrounded = 60.0 / (double)_weapons.Count / (double)_defaultRateOfFire; //set _delay between weapons          
					_delay = (int)Math.Ceiling(delayUnrounded);
					_desiredRPM = 3600.0 / _delay;
				}

				//Checks if guns are being fired
				if (!_isShooting)
				{
					foreach (IMyUserControllableGun thisWeapon in _weapons) //need to track if bool has been reset
					{
						if (thisWeapon.IsShooting && thisWeapon.Enabled)
						{
							_isShooting = true;
							break;
						}
					}
				}

				//===SEQUENCER HANDLING===
				if (_timeCount >= _delay)
				{
					if (!_limitReached)
					{
						////===RESETTING ALL WEAPON STATES===          
						foreach (var thisWeapon in _weapons)
						{
							thisWeapon.Enabled = false;
						}

						//===ACTIVATING SPECIFIED WEAPON===  
						if (_weaponCount >= _weapons.Count)
							_weaponCount = 0; //incase something gets broken

						weaponToFire = _weapons[_weaponCount];
						weaponToFire.Enabled = true;
						_limitReached = true;
					}
					else
					{
						if (weaponToFire.Enabled == false)
							weaponToFire.Enabled = true;
					}

					if (_isShooting)
					{
						if (_weaponCount + 1 < _weapons.Count)
						{
							_weaponCount++; //counts once per _delay
						}
						else
						{
							_weaponCount = 0;
						}
						_timeCount = 0; //start count over
						_isShooting = false;
						weaponToFire.Enabled = false;
						_limitReached = false;

						if (_burstCount > 1 && _shouldBurst)
						{
							_burstCount--;
						}
						else if (_shouldBurst)
						{
							_shouldBurst = false;
							_executeToggle = false;
							_burstCount = 0; //has to be bigger than 0
						}
					}

					if (_executeToggle)
					{
						weaponToFire.ApplyAction("ShootOnce"); //there is no interface method for this :(
						_messageToggle = ">>Toggle Fire Enabled<<";
					}
					else
					{
						_messageToggle = "<<Toggle Fire Disabled>>";
					}
				}
				else
				{
					_timeCount++; //continues to count until _delay is hit	          
				}

				if (_manualOverride)
				{
					_messageOverride = ">>Defaults Overriden<<";
				}
				else
				{
					_messageOverride = "<<Defaults Applied>>";
				}

				//Debug    
				string output = $"----------------------------------\nInfo for '{ThisGroup.Name}' \n{_messageToggle}\n{_messageOverride}\nNo. Weapons: {_weapons.Count}\nRate of Fire: {_desiredRPM} -> {3600.0 / (double)_delay:N1} RPM\nDelay: {_delay} ticks\nCurrent Time: {_timeCount}\nWeapon Count: {_weaponCount}\nIsShooting: {_isShooting}\nBurst Count: {_burstCount}";
				Echo(output);
			}
		}

		/// <summary>
		/// Class that tracks runtime history.
		/// </summary>
		public class RuntimeTracker
		{
			public int Capacity { get; set; }
			public double Sensitivity { get; set; }
			public double MaxRuntime { get; private set; }
			public double MaxInstructions { get; private set; }
			public double AverageRuntime { get; private set; }
			public double AverageInstructions { get; private set; }

			private readonly Queue<double> _runtimes = new Queue<double>();
			private readonly Queue<double> _instructions = new Queue<double>();
			private readonly StringBuilder _sb = new StringBuilder();
			private readonly int _instructionLimit;
			private readonly Program _program;

			public RuntimeTracker(Program program, int capacity = 100, double sensitivity = 0.01)
			{
				_program = program;
				Capacity = capacity;
				Sensitivity = sensitivity;
				_instructionLimit = _program.Runtime.MaxInstructionCount;
			}

			public void AddRuntime()
			{
				double runtime = _program.Runtime.LastRunTimeMs;
				AverageRuntime = Sensitivity * (runtime - AverageRuntime) + AverageRuntime;

				_runtimes.Enqueue(runtime);
				if (_runtimes.Count == Capacity)
				{
					_runtimes.Dequeue();
				}

				MaxRuntime = _runtimes.Max();
			}

			public void AddInstructions()
			{
				double instructions = _program.Runtime.CurrentInstructionCount;
				AverageInstructions = Sensitivity * (instructions - AverageInstructions) + AverageInstructions;

				_instructions.Enqueue(instructions);
				if (_instructions.Count == Capacity)
				{
					_instructions.Dequeue();
				}

				MaxInstructions = _instructions.Max();
			}

			public string Write()
			{
				_sb.Clear();
				_sb.AppendLine("_____________________________\nGeneral Runtime Info\n");
				_sb.AppendLine($"Avg instructions: {AverageInstructions:n2}");
				_sb.AppendLine($"Max instructions: {MaxInstructions:n0}");
				_sb.AppendLine($"Avg complexity: {MaxInstructions / _instructionLimit:0.000}%");
				_sb.AppendLine($"Avg runtime: {AverageRuntime:n4} ms");
				_sb.AppendLine($"Max runtime: {MaxRuntime:n4} ms");
				return _sb.ToString();
			}
		}


	}
}
