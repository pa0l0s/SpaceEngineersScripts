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

		//=======================================================================
		//
		//                      SIMPLE TRADING SCRIPT 
		//                       for Space Engineers
		//                              v1.10
		//                        (c) Pembroke 2018-2019
		//
		//     This script allows you to setup a trading station where the
		//     player can buy/sell resources.
		//
		//     Refer to the "Simple Trading Script Guide" for how to use it.
		//=======================================================================

		const string MODE_BUY = "Buy";
		const string MODE_SELL = "Sell";
		const string MODE_WELCOME = "Welcome";

		struct TradeStation
		{
			public float RateTraderPurchase;
			public string Mode;
			public string ModeTarget;
			public string ItemTypeCurrency;
			public string NameCargoContainerEntry;
			public string NameCargoContainerVault;
			public string NameLCDDisplayCustomer;
			public string NameLCDDisplayPrice;
			public string NameProgrammableBlockScript;
			public string MsgCollectYourItems;
			public string MsgCollectYourPayment;
			public string MsgErrBlockHasNoInventory;
			public string MsgErrBlockNotFound;
			public string MsgErrInsufficientFunds;
			public string MsgErrInsufficientItems;
			public string MsgErrItemsNotReceived;
			public string MsgErrItemsNotSent;
			public string MsgErrNoFunds;
			public string MsgErrNotANumber;
			public string MsgErrNotInterested;
			public string MsgErrNoTradeItems;
			public string MsgErrPaymentNotReceived;
			public string MsgErrPaymentNotSent;
			public string MsgErrPurchaseNotSpecified;
			public string MsgErrTechnicalDifficulties;
			public string MsgErrTransferFailed;
			public string MsgErrTransferInsufficientItemsInSource;
			public string MsgErrTransferNoItemsInSource;
			public string MsgErrUnknownConfig;
			public string MsgErrUnknownMode;
			public string MsgErrUnknownParam;
			public string MsgItem;
			public string MsgPayment;
			public string MsgPricesAreIn;
			public string MsgThatBuys;
			public string MsgTransactionEnds;
			public string MsgWeBuy;
			public string MsgWelcome;
			public string MsgWePay;
			public string MsgWeSell;
			public string MsgYouOffer;
			public IMyCargoContainer ContainerEntry;
			public IMyCargoContainer ContainerVault;
			public IMyTextPanel DisplayCustomer;
			public IMyTextPanel DisplayPrice;
			public IMyProgrammableBlock ProgramScript;
			public IMyInventory InventoryEntry;
			public IMyInventory InventoryVault;
			public HashSet<string> IsWeighable;
			public Dictionary<string, float> PriceLookup;
		};

		TradeStation Info;


		//=======================================================================
		// CONSTRUCTORS

		public Program()
		{
			// The constructor, called only once every session and
			// always before any other method is called. Use it to
			// initialize your script.

			Info.RateTraderPurchase = 0.5F; //purchases from player at 50%
			Info.Mode = MODE_SELL;
			Info.ModeTarget = "Any";
			Info.ItemTypeCurrency = "Uranium";
			Info.NameCargoContainerEntry = "TradeStationEntry";
			Info.NameCargoContainerVault = "TradeStationVault";
			Info.NameLCDDisplayCustomer = "TradeStationCustomerDisplay";
			Info.NameLCDDisplayPrice = "TradeStationPriceDisplay";
			Info.NameProgrammableBlockScript = "TradeStationScript";
			Info.ContainerEntry = null;
			Info.ContainerVault = null;
			Info.DisplayCustomer = null;
			Info.DisplayPrice = null;
			Info.ProgramScript = null;
			Info.InventoryEntry = null;
			Info.InventoryVault = null;
			Info.IsWeighable = new HashSet<string>();
			Info.PriceLookup = new Dictionary<string, float>();

			//Texts used by the script. Can be translated.
			Info.MsgCollectYourItems = "Please, collect your items from the container.";
			Info.MsgCollectYourPayment = "Please, collect your payment from the container.";
			Info.MsgErrBlockHasNoInventory = "ERR: $VAR1$ has no inventory.";
			Info.MsgErrBlockNotFound = "ERR: $VAR1$ does not exist.";
			Info.MsgErrInsufficientFunds = "Sorry, we can't buy that much. We have only ";
			Info.MsgErrInsufficientItems = "Sorry, we can't sell that much. We have only ";
			Info.MsgErrItemsNotReceived = "We couldn't receive the items you offered.";
			Info.MsgErrItemsNotSent = "We couldn't send you your purchase. Refunding your payment.";
			Info.MsgErrNoFunds = "If you want to buy put funds into the container.";
			Info.MsgErrNotANumber = "ERR: Not a number: '$VAR1$'. Check your price overrides.";
			Info.MsgErrNotInterested = "Not interested in buying $VAR1$";
			Info.MsgErrNoTradeItems = "No trade items found. Insert some to sell.";
			Info.MsgErrPaymentNotReceived = "We couldn't receive your payment.";
			Info.MsgErrPaymentNotSent = "Sending your payment failed. Giving your items back.";
			Info.MsgErrPurchaseNotSpecified = "ERR: Keyword 'Any' can't be used when buying.";
			Info.MsgErrTechnicalDifficulties = "Sorry, technical difficulties.";
			Info.MsgErrTransferFailed = "ERR: Failed to move $VAR1$: $VAR2$";
			Info.MsgErrTransferInsufficientItemsInSource = "ERR: Not enough items in $VAR1$: $VAR2$";
			Info.MsgErrTransferNoItemsInSource = "ERR: Can't move items. No $VAR1$ in $VAR2$.";
			Info.MsgErrUnknownConfig = "ERR: Unknown config in $VAR1$ custom data: $VAR2$";
			Info.MsgErrUnknownMode = "ERR: Unknown mode";
			Info.MsgErrUnknownParam = "ERR: Unknown param in $VAR1$: $VAR2$";
			Info.MsgItem = "Item";
			Info.MsgPayment = "Payment";
			Info.MsgPricesAreIn = "Prices are in ";
			Info.MsgThatBuys = "That buys";
			Info.MsgTransactionEnds = "Sale complete. Welcome again.";
			Info.MsgWeBuy = "We buy";
			Info.MsgWelcome = "*** Welcome ***" + System.Environment.NewLine + System.Environment.NewLine + "Ready to trade";
			Info.MsgWePay = "We pay";
			Info.MsgWeSell = "We sell";
			Info.MsgYouOffer = "You offer";
		}


		//=======================================================================
		// HELPER METHODS

		private float ParseFloat(string text)
		{
			//Parses a numeric string into a float, zero if fails
			float parsedValue = 0.0F;
			try
			{
				parsedValue = float.Parse(text);
			}
			catch
			{
				Display(Info.MsgErrNotANumber, text, "");
			}
			return parsedValue;
		}

		private void Display(string text, string var1, string var2, bool append = true)
		{
			string msg = text.Replace("$VAR1$", var1);
			msg = msg.Replace("$VAR2$", var2);
			Display(msg, append);
		}

		private void Display(string text, bool append = true)
		{
			//Display a message on the customer display LCD panel
			if (Info.DisplayCustomer != null)
				Info.DisplayCustomer.WriteText(text + System.Environment.NewLine, append);
		}

		private void ShowPrices()
		{
			//Display all prices on the price display LCD panel
			string priceList =
				Info.MsgPricesAreIn + Info.ItemTypeCurrency + System.Environment.NewLine +
				System.Environment.NewLine +
				Info.MsgItem.PadRight(15) + " " + Info.MsgWeSell.PadLeft(8) + " " + Info.MsgWeBuy.PadLeft(8) + System.Environment.NewLine;
			foreach (KeyValuePair<string, float> KVP in Info.PriceLookup)
			{
				if (KVP.Key == Info.ItemTypeCurrency)
					continue;
				priceList = priceList + System.Environment.NewLine;
				priceList = priceList + KVP.Key.PadRight(15) + " " + KVP.Value.ToString("0.000").PadLeft(8) + " " + (KVP.Value * Info.RateTraderPurchase).ToString("0.000").PadLeft(8);
			}
			Info.DisplayPrice.WriteText(priceList);
		}

		private void SetLookup(Dictionary<string, float> lookupTable, string key, float value)
		{
			if (lookupTable.ContainsKey(key))
				lookupTable.Remove(key);
			if (value >= 0.001)
				lookupTable.Add(key, value);
		}

		private string GetItemType(MyInventoryItem item)
		{
			string typeOfItem = item.Type.SubtypeId.ToString();
			string contentDescr = item.ToString();
			if (contentDescr.Contains("_Ore"))
			{
				if (typeOfItem != "Stone" && typeOfItem != "Ice")
					typeOfItem = typeOfItem + " Ore";
			}
			if (typeOfItem == "Stone" && contentDescr.Contains("_Ingot"))
				typeOfItem = "Gravel";
			return typeOfItem;
		}

		private List<MyInventoryItem> GetInventoryItems(IMyInventory container)
		{
			List<MyInventoryItem> items = new List<MyInventoryItem>();
			container.GetItems(items);
			return items;
		}

		//=======================================================================
		// SETUP AND CONFIG METHODS

		private bool SetupDisplayCustomer()
		{
			//Customer display is used for error messages so have a separate setup for it to get it up ASAP.
			Info.DisplayCustomer = GridTerminalSystem.GetBlockWithName(Info.NameLCDDisplayCustomer) as IMyTextPanel;
			if (Info.DisplayCustomer == null)
				return false;

			Info.DisplayCustomer.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
			return true;
		}

		private bool SetupProgramScript()
		{
			//The programmable block's customer data contains the configuration so setup this first
			Info.ProgramScript = GridTerminalSystem.GetBlockWithName(Info.NameProgrammableBlockScript) as IMyProgrammableBlock;
			if (Info.ProgramScript == null)
				return false;

			return true;
		}

		private void SetDefaultPrices()
		{
			//Wikia:
			//  Platinum Ore to Platinum Ingots:  0.48%
			//  Uranium Ore to Uranium Ingots:    0.70%
			//  Gold Ore to Gold Ingots:          1.00%
			//  Siver Ore to Silver Ingots:      10.00%
			//  Cobalt Ore to Cobalt Ingots:     24.00% (Arc Furnace can be used)
			//  Nickel Ore to Nickel Ingots:     40.00% (Arc Furnace can be used)
			//  Silicon Ore to Silicon Wafers:   70.00%
			//  Iron Ore to Iron Ingots:         70.00% (Arc Furnace can be used)
			//  Stone to Gravel                  90.00%
			SetLookup(Info.PriceLookup, "Platinum", 30.0F); //1 Platinum costs 30 Uranium
			SetLookup(Info.PriceLookup, "Gold", 10.0F); //1 Gold costs 10 Uranium
			SetLookup(Info.PriceLookup, "Silver", 3.0F); //1 Silver costs 3 Uranium
			SetLookup(Info.PriceLookup, "Uranium", 1.0F); //1 Uranium costs 1 Uranium, currency resource
			SetLookup(Info.PriceLookup, "Nickel", 0.3F); //1 Nickel costs 0.3 Uranium
			SetLookup(Info.PriceLookup, "Cobalt", 0.3F); //1 Cobalt costs 0.3 Uranium
			SetLookup(Info.PriceLookup, "Silicon", 0.1F); //1 Silicon costs 0.1 Uranium
			Info.IsWeighable.Add("Platinum");
			Info.IsWeighable.Add("Gold");
			Info.IsWeighable.Add("Silver");
			Info.IsWeighable.Add("Uranium");
			Info.IsWeighable.Add("Nickel");
			Info.IsWeighable.Add("Cobalt");
			Info.IsWeighable.Add("Silicon");
			Info.IsWeighable.Add("Iron");
			Info.IsWeighable.Add("Gravel");
			Info.IsWeighable.Add("Platinum Ore");
			Info.IsWeighable.Add("Gold Ore");
			Info.IsWeighable.Add("Silver Ore");
			Info.IsWeighable.Add("Uranium Ore");
			Info.IsWeighable.Add("Nickel Ore");
			Info.IsWeighable.Add("Cobalt Ore");
			Info.IsWeighable.Add("Silicon Ore");
			Info.IsWeighable.Add("Iron Ore");
			Info.IsWeighable.Add("Stone");
			Info.IsWeighable.Add("Ice");
			Info.RateTraderPurchase = 0.5F; //trader buys from player at 50%
		}

		private bool Setup()
		{
			//Setup all the other blocks in the system

			//Price display
			Info.DisplayPrice = GridTerminalSystem.GetBlockWithName(Info.NameLCDDisplayPrice) as IMyTextPanel;
			if (Info.DisplayPrice == null)
			{
				Display(Info.MsgErrBlockNotFound, Info.NameLCDDisplayPrice, "");
				return false;
			}
			Info.DisplayPrice.Font = "Monospace";

			//Player interacts with this cargo container
			Info.ContainerEntry = GridTerminalSystem.GetBlockWithName(Info.NameCargoContainerEntry) as IMyCargoContainer;
			if (Info.ContainerEntry == null)
			{
				Display(Info.MsgErrBlockNotFound, Info.NameCargoContainerEntry, "");
				return false;
			}

			//All the items the trading station has for offer stored in this cargo container
			Info.ContainerVault = GridTerminalSystem.GetBlockWithName(Info.NameCargoContainerVault) as IMyCargoContainer;
			if (Info.ContainerVault == null)
			{
				Display(Info.MsgErrBlockNotFound, Info.NameCargoContainerVault, "");
				return false;
			}

			//The inventory of the "Entry" cargo container
			Info.InventoryEntry = (Info.ContainerEntry as IMyEntity).GetInventory(0);
			if (Info.InventoryEntry == null)
			{
				Display(Info.MsgErrBlockHasNoInventory, Info.NameCargoContainerEntry, "");
				return false;
			}

			//The inventory of the "Vault" cargo container
			Info.InventoryVault = (Info.ContainerVault as IMyEntity).GetInventory(0);
			if (Info.InventoryVault == null)
			{
				Display(Info.MsgErrBlockHasNoInventory, Info.NameCargoContainerVault, "");
				return false;
			}

			return true;
		}

		private bool ApplyConfig(string paramType, string paramKey, string paramValue)
		{
			//Applies a single config. Separate becase called from two places as 
			//it handles both custom data config and script parameters.
			bool fgSuccess = true;
			if (paramType == "Config")
			{
				switch (paramKey)
				{
					case "Currency":
						Info.ItemTypeCurrency = paramValue;
						break;
					case "CustomerDisplay":
						Info.NameLCDDisplayCustomer = paramValue;
						SetupDisplayCustomer(); //once we know this setup it immediately for possible error messages
						break;
					case "PriceDisplay":
						Info.NameLCDDisplayPrice = paramValue;
						break;
					case "Entry":
						Info.NameCargoContainerEntry = paramValue;
						break;
					case "PurchaseRate":
						Info.RateTraderPurchase = ParseFloat(paramValue);
						break;
					case "Vault":
						Info.NameCargoContainerVault = paramValue;
						break;
					case "Welcome":
						Info.MsgWelcome = paramValue.Replace("<BR>", System.Environment.NewLine);
						break;
					default:
						Display(Info.MsgErrUnknownConfig, Info.NameProgrammableBlockScript, paramType + ":" + paramKey + "=" + paramValue);
						fgSuccess = false;
						break;
				}
			}
			else if (paramType == "Param")
			{
				switch (paramKey)
				{
					case "Mode":
						if (paramValue == MODE_BUY || paramValue == MODE_SELL || paramValue == MODE_WELCOME)
							Info.Mode = paramValue;
						else
							Display(Info.MsgErrUnknownMode + ": " + paramValue);
						break;
					case "Script":
						Info.NameProgrammableBlockScript = paramValue;
						SetupProgramScript(); //once we know this setup it immediately as it contains the rest of the configs
						break;
					case "Target":
						Info.ModeTarget = paramValue;
						break;
					default:
						Display(Info.MsgErrUnknownParam, Info.NameProgrammableBlockScript, paramType + ":" + paramKey + "=" + paramValue);
						fgSuccess = false;
						break;
				}
			}
			else if (paramType == "Price")
			{
				//Custom trade item price
				float price = ParseFloat(paramValue);
				SetLookup(Info.PriceLookup, paramKey, price);
			}
			else if (paramType == "Weighable")
			{
				//Item type can be divided into chunks of 0.000001 units.
				if (paramValue == "1")
					Info.IsWeighable.Add(paramKey);
				else
					Info.IsWeighable.Remove(paramKey);
			}
			return fgSuccess;
		}

		private bool Configure()
		{
			//Reads the custom data from the programmable block to configure the system
			bool fgSuccess = true;
			char[] sepLine = { ';' };
			char[] sepPars = { ':', '=' };
			string configText = Info.ProgramScript.CustomData;
			string[] configLines = configText.Split(sepLine);
			string[] paramParts;
			string paramType;
			string paramKey;
			string paramValue;
			SetDefaultPrices();
			if (configLines != null)
			{
				int i;
				for (i = 0; i < configLines.Length; i++)
				{
					paramParts = configLines[i].Split(sepPars);
					if (paramParts.Length < 3)
						continue;
					paramType = paramParts[0].Trim();
					paramKey = paramParts[1].Trim();
					paramValue = paramParts[2].Trim();
					if (!ApplyConfig(paramType, paramKey, paramValue))
						fgSuccess = false;
				}
			}
			return fgSuccess;
		}


		//=======================================================================
		// INVENTORY MANAGEMENT METHODS

		private int IndexOfFirstTradeItem(IMyInventory container, string requiredType, bool verbose)
		{
			//Finds the first item in the given container that is of the given requiredType
			//and that can be traded i.e. that has a price. Give requiredType="Any" to match
			//any type except the currency resource type.
			//Returns its inventory index or -1 if none found.

			int ix = -1;

			List<MyInventoryItem> containerItems = GetInventoryItems(container);
			if (containerItems.Count > 0)
			{
				bool fgTypeMatches;
				int i;
				string itemType;
				for (i = 0; i < containerItems.Count; i++)
				{
					itemType = GetItemType(containerItems[i]);
					if (requiredType == itemType)
						fgTypeMatches = true;
					else if (requiredType == "Any" && itemType != Info.ItemTypeCurrency)
						fgTypeMatches = true;
					else
						fgTypeMatches = false;
					if (fgTypeMatches)
					{
						if (Info.PriceLookup.ContainsKey(itemType))
						{
							ix = i;
							break;
						}
						else if (verbose)
							Display(Info.MsgErrNotInterested, itemType, "");
					}
				}
			}
			return ix;
		}

		private bool Transfer(IMyInventory source, IMyInventory destination, string itemType, VRage.MyFixedPoint amount)
		{
			//Moves given amount of given type from source to destination
			int ixSource;
			List<MyInventoryItem> sourceItems = GetInventoryItems(source);

			ixSource = IndexOfFirstTradeItem(source, itemType, false);
			if (ixSource < 0)
			{
				Display(Info.MsgErrTransferNoItemsInSource, itemType, source.ToString());
				return false;
			}

			MyInventoryItem item = sourceItems[ixSource];
			if (amount > item.Amount)
			{
				Display(Info.MsgErrTransferInsufficientItemsInSource, source.ToString(), item.Amount + "/" + amount.ToString() + " " + itemType);
				return false;
			}

			if (!source.TransferItemTo(destination, ixSource, null, true, amount))
			{
				Display(Info.MsgErrTransferFailed, amount.ToString() + " " + itemType, source.ToString() + " -> " + destination.ToString());
				return false;
			}

			return true;
		}


		//=======================================================================
		// TRADING METHODS

		private bool Buy(string requiredType)
		{
			//Player buys from the trader
			//Buys the first item in the vault cargo container that matches
			//the given type. The type "Any" can't be used.

			int ixTradeItem;
			int ixPaymentItem;
			List<MyInventoryItem> entryItems;
			List<MyInventoryItem> vaultItems;

			if (Info.ModeTarget == "Any")
			{
				Display(Info.MsgErrPurchaseNotSpecified);
				return false;
			}

			//Find the currency item the player is offering
			ixPaymentItem = IndexOfFirstTradeItem(Info.InventoryEntry, Info.ItemTypeCurrency, false);
			if (ixPaymentItem < 0)
			{
				Display(Info.MsgErrNoFunds + " (" + Info.ItemTypeCurrency + ")");
				return false;
			}

			//Fetch the currency item the player is offering
			entryItems = GetInventoryItems(Info.InventoryEntry);
			MyInventoryItem paymentItem = entryItems[ixPaymentItem];
			string paymentItemType = GetItemType(paymentItem);
			VRage.MyFixedPoint paymentItemAmount = paymentItem.Amount;
			Display(Info.MsgYouOffer + ": " + paymentItemAmount.ToString() + " " + paymentItemType);

			//Calculate the amount the player gets
			string tradeItemType = requiredType;
			VRage.MyFixedPoint tradeItemAmount = paymentItemAmount * (1.0F / Info.PriceLookup[tradeItemType]);
			if (!Info.IsWeighable.Contains(tradeItemType))
			{
				//If purchased item can't be divided adjust the amount to whole units and recalc the price
				tradeItemAmount = VRage.MyFixedPoint.Floor(tradeItemAmount);
				paymentItemAmount = tradeItemAmount * Info.PriceLookup[tradeItemType];
			}
			Display(Info.MsgThatBuys + ": " + tradeItemAmount.ToString() + " " + tradeItemType);

			//Find the item the player is buying
			ixTradeItem = IndexOfFirstTradeItem(Info.InventoryVault, tradeItemType, false);
			if (ixTradeItem < 0)
			{
				Display(Info.MsgErrInsufficientItems + "0 " + requiredType + ".");
				return false;
			}

			//Fetch the item the player purchased
			vaultItems = GetInventoryItems(Info.InventoryVault);
			MyInventoryItem tradeItem = vaultItems[ixTradeItem];
			tradeItemType = GetItemType(tradeItem);
			VRage.MyFixedPoint maxTradeItemAmount = tradeItem.Amount;

			if (tradeItemAmount > maxTradeItemAmount)
			{
				Display(Info.MsgErrInsufficientItems + maxTradeItemAmount.ToString() + " " + tradeItemType + ".");
				return false;
			}

			//Move the payment to the vault
			if (!Transfer(Info.InventoryEntry, Info.InventoryVault, paymentItemType, paymentItemAmount))
			{
				Display(Info.MsgErrTechnicalDifficulties + " " + Info.MsgErrPaymentNotReceived);
				return false;
			}
			Display(Info.MsgPayment + ": " + paymentItemAmount.ToString() + " " + paymentItemType);

			//Move the item to the "Entry" container
			if (!Transfer(Info.InventoryVault, Info.InventoryEntry, tradeItemType, tradeItemAmount))
			{
				//Moving the purchase failed so return the payment back to the player, if this fails then too bad...
				Display(Info.MsgErrTechnicalDifficulties + " " + Info.MsgErrItemsNotSent);
				Transfer(Info.InventoryVault, Info.InventoryEntry, paymentItemType, paymentItemAmount);
				return false;
			}
			Display(Info.MsgCollectYourItems);
			Display(Info.MsgTransactionEnds);

			return true;
		}

		private bool Sell(string requiredType)
		{
			//Player sells to the trader
			//Sells the first item in the "Entry" cargo container that matches
			//the given type. Use "Any" to match any type (that has a price).

			int ixTradeItem;
			int ixPaymentItem;
			List<MyInventoryItem> entryItems;
			List<MyInventoryItem> vaultItems;

			//Find the item the player is selling
			ixTradeItem = IndexOfFirstTradeItem(Info.InventoryEntry, requiredType, true);
			if (ixTradeItem < 0)
			{
				Display(Info.MsgErrNoTradeItems);
				return false;
			}

			//Fetch the item the player sells
			entryItems = GetInventoryItems(Info.InventoryEntry);
			MyInventoryItem tradeItem = entryItems[ixTradeItem];
			string tradeItemType = GetItemType(tradeItem);
			VRage.MyFixedPoint tradeItemAmount = tradeItem.Amount;
			Display(Info.MsgYouOffer + ": " + tradeItemAmount.ToString() + " " + tradeItemType);

			//Calculate the payment
			float price = Info.PriceLookup[tradeItemType] * Info.RateTraderPurchase;
			VRage.MyFixedPoint paymentItemAmount = tradeItemAmount * price;
			Display(Info.MsgWePay + ": " + paymentItemAmount.ToString() + " " + Info.ItemTypeCurrency);

			//Find the payment item in the vault
			ixPaymentItem = IndexOfFirstTradeItem(Info.InventoryVault, Info.ItemTypeCurrency, false);
			if (ixPaymentItem < 0)
			{
				Display(Info.MsgErrInsufficientFunds + "0 " + Info.ItemTypeCurrency + ".");
				return false;
			}

			//Fetch the payment item in the vault
			vaultItems = GetInventoryItems(Info.InventoryVault);
			MyInventoryItem paymentItem = vaultItems[ixPaymentItem];
			string paymentItemType = GetItemType(paymentItem);
			VRage.MyFixedPoint maxPaymentItemAmount = paymentItem.Amount;

			if (paymentItemAmount > maxPaymentItemAmount)
			{
				Display(Info.MsgErrInsufficientFunds + maxPaymentItemAmount.ToString() + " " + paymentItemType + ".");
				return false;
			}

			//Move the player's item to vault
			if (!Transfer(Info.InventoryEntry, Info.InventoryVault, tradeItemType, tradeItemAmount))
			{
				Display(Info.MsgErrTechnicalDifficulties + " " + Info.MsgErrItemsNotReceived);
				return false;
			}

			//Pay the player
			if (!Transfer(Info.InventoryVault, Info.InventoryEntry, paymentItemType, paymentItemAmount))
			{
				//Payment failed so return the trade item back to the player, if this fails then too bad...
				Display(Info.MsgErrTechnicalDifficulties + " " + Info.MsgErrPaymentNotSent);
				Transfer(Info.InventoryVault, Info.InventoryEntry, tradeItemType, tradeItemAmount);
				return false;
			}
			Display(Info.MsgCollectYourPayment);
			Display(Info.MsgTransactionEnds);

			return true;
		}


		//=======================================================================
		// MAIN ENTRY POINT FOR SCRIPT

		public void Main(string args)
		{
			// The main entry point of the script, invoked every time
			// one of the programmable block's Run actions are invoked.

			char[] sepArgs = { ',' };
			string[] listArgs = args.Split(sepArgs);

			//1st argument is the name of the programmable block that runs this script
			if (listArgs.Length > 0)
				ApplyConfig("Param", "Script", listArgs[0]);

			//Now we know the programmable block, read its config and setup the system
			//If fails then just abort as there's nothing that can be done
			if (!Configure())
				return;
			if (!Setup())
				return;

			//2nd argument is the mode: "Buy", "Sell", or "Welcome"
			if (listArgs.Length > 1)
				ApplyConfig("Param", "Mode", listArgs[1]);

			//3rd argument is the mode target
			if (listArgs.Length > 2)
				ApplyConfig("Param", "Target", listArgs[2]);

			//Clear the customer screen
			Display("", false);

			//Write price list
			ShowPrices();

			switch (Info.Mode)
			{
				case MODE_BUY:
					Buy(Info.ModeTarget);
					break;
				case MODE_SELL:
					Sell(Info.ModeTarget);
					break;
				case MODE_WELCOME:
					Display(Info.MsgWelcome, false);
					break;
				default:
					Display(Info.MsgErrUnknownMode + ": " + Info.Mode + "='" + Info.ModeTarget + "'");
					break;
			}
		}


		//=======================================================================
		// SAVE PERSISTENT DATA

		public void Save()
		{
			// Called when the program needs to save its state. Use
			// this method to save your state to the Storage field
			// or some other means.

			// This method is optional and can be removed if not
			// needed.
		}

		//=======================================================================
		//////////////////////////END////////////////////////////////////////////
		//=======================================================================

	}
}
