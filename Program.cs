using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript {
    partial class Program : MyGridProgram {
        // Kristofs Lazy Drill Script 
        string version = "Version 1.5";
        // This script is intended to be a generic script to drive a DRILL grid with 2 piston groups and a rotor.
        // Read the info in the workshop about its features.

        //IMPORTANT SETUP INFO
        //********************

        // As from version 1.2.  The variables below will be put into the CUSTOM DATA of the programmable block at first run.
        // You can update the variables in the CUSTOM DATA if you want to override the defaults below. 
        // This will prevent you from losing YOUR settings when the script would be updated. 
        // You can of course still adapt the defaults below as well, they will be put in the CUSTOM DATA when you clear the existing one, but overwritten by a script update. 

        // Default values of the script below
        // You are recommended to have an LCD for status info, if none is provided or found, the script will fall back to the PB display.  Set the variable below to your LCD name
        string LCD_Name = "lazydrills";
        int DisplayNum = 0; // Desired display to use on the block, ex. 0 for main display on PB, 1 for keyboard, defaults to 0 if an invalid number is used.
                            // You MUST have a rotor on the grid with the name set equal to the variable below. 
                            // This rotor MUST have a mininum and maximum limit set.  I recommend min 0 and max 359.  Min can be negative as well. 
        string DrillRotor = "Rotor";
        string Pistongroup1name = "down";  //Having this group with pistons is a MUST. 
        string Pistongroup2name = "outward";  //Having this group is optional. It allows another movement after all pistons of 1 are extended. 
        string Drillgroupname = "drills";  //Having this group with your drills is a MUST

        string Cargocontainergroupname = "orecargo";  // Optional :  if you want the drill to pauze when a group of containers reaches a % fill, provide its group name here.
        // Do NOT remove this variables if you do not want to use them, this would cause errors.  Just leave them and have no container group with the name specified.
        float Cargofillallowed = (float)90;  // Percentage the cargo can be filled. If higher the drill will pauze.  Setting to 100 means to ignore it.  

        // You can set the speed of the rotor with the 2 variables below.
        // Note that the script will detect if ore is drilled by checking your drills inventory. 
        // If no ore is found, it increases the speed until the max the speed set in the rotorspeedfast variable.
        // To disable this, set the fast speed equal to the slow speed. 
        // The rotor will speed up and slow down when far or close to the min and max to assure a safe stop.
        float rotorspeed = (float)0.5;
        float rotorspeedfast = (float)2;

        float extendamountpiston1 = (float)0.5;  // how much meters to extend ONE piston of group 1 each time ? 
        float extendamountpiston2 = (float)5;  // how much meters to extend ONE piston of group 2 each time ? 
        float pistonspeed = (float)0.3;  // the speed at which a piston should extend

        //Commands you can use:

        // reset :  all pistons are reset and retracted and settings are reloaded.  
        // start :  starts or continues drilling (not possible if the state is error)
        // stop :   stops the drills and the rotor.  
        //          When stopped, you can make changes if you want, like manually extending pistons or even adding pistons and drills. 
        //          When start is used, the code will find the changes and continue drilling using the changes.  Pistons are not retracted. 
        // retract1 retract2 retractall :  Retract ALL pistons from group1, group2 or both groups
        // extend1 extend2  :  extend one piston of the group1 or 2 using the extendamount set above.  Use this to manual position the drills when STOPPED if you wish
        // ALWAYS use STOP before making any changes, and START again after. 

        // ********************************************
        // Changes below this line are on your own risk. 

        string state = "none";
        IMyMotorStator MyRotor;
        string waitfor = "0";  // Direction of rotor follow up.
        IMyPistonBase ThePiston;
        List<IMyPistonBase> Pistongroup1 = new List<IMyPistonBase>();
        List<IMyPistonBase> Pistongroup2 = new List<IMyPistonBase>();
        List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
        IMyBlockGroup blocksgroup;
        List<IMyShipDrill> Drillgroup = new List<IMyShipDrill>();
        long extendingpiston;  // Id of the piston that needs extending

        //Vars for the LCD
        IMyTerminalBlock displayBlock;
        IMyTextSurface textSurface, PBtextSurface;
        IMyTextSurfaceProvider textSurfaceProvider;
        int displaySelect;
        string lcdtxt = "";  // State info
        string lcdtxt2 = "";
        string lcdtxt3 = "";
        string lcdtxt4 = "";
        string lcdtxt5 = "";
        string lcdtxt6 = "";
        string lcdtxt7 = "";  // drill usage
        string lcdtxt8 = "";
        string lcdtxt9 = "";  //movement stuck warning
        string lcdtxt10 = ""; // container fill level
        string lcdtxt11 = "";  // feedback ismovementdone

        bool drillsaredirty = false;
        MyIni _ini = new MyIni();
        int extendtimer = 0;  // How long does extending take 
        int rotortimer = 0;  // Checking if the rotor is turning
        float rotorcheckpoint = 0; // used to store a checkpoint to detect movement
        bool alldone;
        string nextaction = "none";  // used to know what would be the next action after the extend is done.  
        string star = "/";
        int slowdown = 0;
        bool waitingretraction = false;  //needed for recovery to know if we are waiting for a retracting piston already
        double progressbaseline = 0; // used for kickstart to see if pistons still move down
        int pistontimer = 0; //used for kickstart testing piston movement
        double containermaxcap = 0; // used to calculate max capacity we can fill
        bool containercheck = true;  //used to do or not do the container check
        List<IMyCargoContainer> DrillContainers = new List<IMyCargoContainer>();


        public Program() {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
            if (Storage.Length > 0) { state = Storage.ToString(); } else { state = "none"; }
            Init();
        }

        public void Save() { Storage = state; }

        public void Main(string argument, UpdateType updateSource) {
            //Random rnd = new Random();
            alldone = true;
            switch (argument.ToLower()) {
                case "reset":
                    state = "none";
                    Init();
                    pistoncheck(); // Set all pistons to be used to min and max 0
                    break;
                case "start":
                    if (state == "error") {
                        lcdtxt3 = "Solve ERRORS first and use command 'reset' to try again, 'start' command can't be used until this is solved";
                        break;
                    }
                    rotortimer = 0;
                    Init();  // lets call init to check if something has changed
                    drillit();
                    break;
                case "retract1":
                    retract("group1");
                    break;
                case "retract2":
                    retract("group2");
                    break;
                case "retractall":
                    retract("group1");
                    retract("group2");
                    break;
                case "stop":
                    stop();
                    break;
                case "readini":
                    ReadIni();
                    break;
                case "makeini":
                    MakeIni();
                    break;
                case "extend1":
                    if (state != "none") {
                        lcdtxt8 = "extend command can only be used in state 'none'.  Use 'stop' first. ";
                        break;
                    }
                    extendsinglepiston("group1");
                    break;
                case "extend2":
                    if (state != "none") {
                        lcdtxt8 = "extend command can only be used in state 'none'.  Use 'stop' first. ";
                        break;
                    }
                    extendsinglepiston("group2");
                    break;

                case "kickstart":
                    stop();  // make sure the drill is stopped
                    state = "kickstarting";
                    progressbaseline = progress(); // initial length measurement
                    extendsinglepiston("group1");  // do initial extend
                    pistontimer = 0;
                    slowdown = 0;
                    // rest is done in state kickstarting below
                    break;
            }  //end of status CASE 
            updatelcd();

            switch (state) {
                case "error":

                    break;

                case "pausedrill":   //pause because drills are full
                    lcdtxt = "State:  Paused because drills inventory is full - risk of explosion.";
                    if (Drillfill() < 99) { state = "drilling"; }
                    break;

                case "pausecontainer":  //pauze because containers are above max allowed
                    lcdtxt = "State:  Paused because Container is full - risk of explosion.";
                    MyRotor.RotorLock = true;
                    if ((Containerfill() < Cargofillallowed)) { state = "drilling"; MyRotor.RotorLock = false; }
                    break;

                case "none":  //Nothing happening at the moment
                    if (slowdown == 10) {
                        if (star == "/") { star = "-"; } else { if (star == "|") { star = "/"; } else { if (star == "-") { star = "\\"; } else { if (star == "\\") { star = "|"; } } } }
                        slowdown = 0;
                    } else { slowdown++; }
                    lcdtxt = "State : none";
                    lcdtxt3 = "";
                    lcdtxt4 = "Waiting orders...." + star + "\n\nCommands that could be issued in the PB are:  \nreset -> Pistons are reset and retracted and settings are reloaded.\nstart -> starts or continues drilling.\n";
                    lcdtxt5 = "";
                    lcdtxt6 = "";

                    break;

                case "kickstarting":

                    if (Ismovementdone() == true) { extendsinglepiston("group1"); pistontimer = 0; progressbaseline = progress(); }  //The extention is done so we did not hit the ground,  extend another time, and restart checks
                    else
                    if (slowdown == 10) {
                        {
                            if (progress() > progressbaseline) { pistontimer = 0; progressbaseline = progress(); } // we did not reach the end, but we did move, so we wait a bit more
                            else { // we did not move but did not reach the end. Could be the ground if this is confirmed a couple of times
                                pistontimer++;
                                if (pistontimer > 20) {
                                    stop();
                                    state = "none";
                                    ThePiston = GridTerminalSystem.GetBlockWithId(extendingpiston) as IMyPistonBase;
                                    ThePiston.MinLimit = ThePiston.CurrentPosition - (float)0.5;
                                    ThePiston.Velocity = -pistonspeed;
                                    ThePiston.Retract();
                                }  // 
                                   // if we reach here,  we just wait a bit more
                            }
                        }
                        slowdown = 0;
                    } else { slowdown++; }
                    lcdtxt = "State: kickstarting.  Moving drill until ground level. ";
                    break;


                case "recovering":
                    // retract a piston a bit 
                    lcdtxt = "Recovering from stuck drill head, retracting a bit";
                    if (waitingretraction == true && Ismovementdone() == true) { state = "drilling"; waitingretraction = false; }   // we were waiting already for a retracting piston and it is done
                    else {// we were not waiting, or it is not done yet
                        if (waitingretraction == false) {//we are not waiting yet, action is needed
                            if (retractsinglepiston(Pistongroup1) == true) { waitingretraction = true; }  //we called the action and can start waiting now
                            else { // We can't retract any more !!!  Now we need a human. 
                                state = "error"; lcdtxt2 = "ERROR : rotor is stuck and we \ncan't retract the pistons higher. \nHuman Intervention needed. \n Use start to try drilling again.";
                            }
                        }
                    }
                    break;

                case "drilling":
                    // Check if we do move
                    if (rotortimer == 0) { rotorcheckpoint = (float)Math.Round(MyRotor.Angle, 3); }
                    rotortimer++;
                    if (rotortimer > 50) {
                        if (rotorcheckpoint == (float)Math.Round(MyRotor.Angle, 3)) { //we did not move for 5 secs
                            state = "recovering";
                            rotortimer = 0;
                            lcdtxt9 = " Movement check: ALERT";
                            break;
                        } else { // we did move, reset check
                            rotortimer = 0;
                            lcdtxt9 = "";
                        }
                    }

                    // did the rotor reach the end of a turn ? 
                    versnelling();  //determine speed
                    lcdtxt = "State : Drilling " + lcdtxt9;
                    lcdtxt4 = "Commands that could be issued in the PB are:  \nstop -> Pauze drilling. This allows you to change settings and even add pistons, drills. \nWhen done, use START to continue drilling. \n";
                    if (Drillfill() >= 99) { state = "pausedrill"; break; };

                    if (containercheck == true) {
                        if ((Containerfill() >= Cargofillallowed)) { state = "pausecontainer"; break; };
                    }

                    if (((Math.Round(MyRotor.Angle * 1000) == Math.Round(MyRotor.UpperLimitRad * 1000)) && waitfor == "360") || (Math.Round(MyRotor.Angle * 1000) == Math.Round(MyRotor.LowerLimitRad * 1000)) && waitfor == "0") //We are waiting for the drills to reach the max end of pistons
                    {
                        FindPiston(Pistongroup1);  // will find a piston that is not DONE.  returns the pistonID and the alldone false/true
                        if (alldone == false) { nextaction = "drilling"; extend("group1"); }  //not done yet, extend this piston that we found
                        else {
                            if (Pistongroup2.Count() == 0)  //IF no grouo 2, we are done 100%
                            { state = "none"; lcdtxt = "No group 2. We are done."; } else // There is a group2, we need to check if that is done as well
                                                                                     {
                                FindPiston(Pistongroup2);  // Wild find a piston that is not DONE.  returns the pistonID and the alldone false/true

                                if (alldone == false) { retract("group1"); }  //not done yet, extend this piston that we found from group2
                                else { state = "none"; lcdtxt = "Group 2 all done. We are done."; } // All is done !!!
                            }
                        }
                    }
                    break;

                case "retracting":
                    lcdtxt = "State: retracting";
                    if (Ismovementdone() == true) { nextaction = "drilling"; extend("group2"); }// extend ThePiston ? 
                    break;

                case "extending":
                    lcdtxt = "State: extending for " + extendtimer / 60 + " seconds\n" + lcdtxt11;
                    extendtimer++;
                    if (Ismovementdone() == true) {
                        if (nextaction == "drilling") {  //switch rotor direction
                            if (waitfor == "0") { waitfor = "360"; } else { waitfor = "0"; }
                            state = "drilling";
                            extendtimer = 0;
                        } // end switch rotor

                        if (nextaction == "none") { state = "none"; }
                    }
                    break;
            }  // end of state
        }  // end of MAIN

        public bool Ismovementdone() {
            bool alldone = true;
            for (int i = 0; i < Pistongroup1.Count(); i++) {
                if (Pistongroup1[i].CurrentPosition != Pistongroup1[i].MaxLimit)   //If this one is not retracted we might be not done with retracting
                {
                    lcdtxt11 = "Piston group 1: " + Pistongroup1[i].CustomName + " is at " + Math.Round(Pistongroup1[i].CurrentPosition, 2) + " of " + Pistongroup1[i].MaxLimit;
                    alldone = false;
                    break;
                }
            }
            if (Pistongroup2 != null) {
                for (int i = 0; i < Pistongroup2.Count(); i++) {
                    if (Pistongroup2[i].CurrentPosition != Pistongroup2[i].MaxLimit)   //If this one is not retracted we might be not done with retracting
                    {
                        lcdtxt11 = "Piston group 2: " + Pistongroup2[i].CustomName + " is at " + Math.Round(Pistongroup2[i].CurrentPosition, 2) + " of " + Pistongroup2[i].MaxLimit;
                        alldone = false;
                        break;
                    } // wait a bit more, this one is not at 0 yet, and we can quit loop
                }
            }
            if (alldone == true) { lcdtxt11 = ""; }  // if all is done remove the piston info lcdtxt11
            return alldone;
        }


        public void Init() {
            ReadIni();
            lcdtxt2 = "Variables can be changed in CUSTOM DATA of the Programmable block";
            lcdtxt3 = "";
            lcdtxt4 = "";
            lcdtxt5 = "";
            lcdtxt8 = "";

            // Is the LCD present and determine the screen to use
            if (GridTerminalSystem.GetBlockWithName(LCD_Name) != null) {
                displayBlock = GridTerminalSystem.GetBlockWithName(LCD_Name);
                if (displayBlock is IMyTextPanel) { textSurface = displayBlock as IMyTextPanel; } else {
                    textSurfaceProvider = (IMyTextSurfaceProvider)displayBlock;
                    if (DisplayNum < (textSurfaceProvider.SurfaceCount - 1)) { displaySelect = 0; } else { displaySelect = DisplayNum; }
                    textSurface = textSurfaceProvider.GetSurface(displaySelect);
                }
                // Now getting the display of the PB as second screen for essential info only. 
                displayBlock = Me;
                textSurfaceProvider = (IMyTextSurfaceProvider)displayBlock;
                if (DisplayNum < (textSurfaceProvider.SurfaceCount - 1)) { displaySelect = 0; } else { displaySelect = DisplayNum; }
                PBtextSurface = textSurfaceProvider.GetSurface(displaySelect);
            } else {
                Echo("ERROR: No LCD found with name " + LCD_Name + "\nDefaulting to PB Display...  \n STRONGLY recommended to build a \nWIDE LCD screen with this name ");
                displayBlock = Me;
                textSurfaceProvider = (IMyTextSurfaceProvider)displayBlock;
                if (DisplayNum < (textSurfaceProvider.SurfaceCount - 1)) { displaySelect = 0; } else { displaySelect = DisplayNum; }
                textSurface = textSurfaceProvider.GetSurface(displaySelect);
            }
            textSurface.ContentType = ContentType.TEXT_AND_IMAGE;
            textSurface.FontSize = 0.8f;


            if (PBtextSurface != null) { PBtextSurface.WriteText("", false); }

            // Is the ROTOR present ?  If YES, does it have a min and max set ? 

            if (GridTerminalSystem.GetBlockWithName(DrillRotor) != null) {  // we have a rotor !   
                MyRotor = GridTerminalSystem.GetBlockWithName(DrillRotor) as IMyMotorStator;
                Echo("Rotor (" + DrillRotor + ") was found.  ");
                if ((MyRotor.LowerLimitDeg == float.MinValue) || (MyRotor.UpperLimitDeg == float.MaxValue)) {  //it does not have a min and max set
                    Echo("ERROR: Rotor does not have an explicit \nmin and max value set.  They can't be infinite");
                    state = "error";
                    lcdtxt2 += "\n ERROR: Rotor does not have a min and max value set.  They can't be infinite. Please set a value like 0 and 359";
                } else  //Rotor is found and has min and max, so all ok. 
                  {
                    if (MyRotor.UpperLimitDeg <= MyRotor.LowerLimitDeg) { // max bigger then min means EXPLODE. so lets stop that
                        Echo("ERROR: Rotor MIN >= MAX. This will explode the rotor. Please change.");
                        state = "error";
                        lcdtxt2 += "\n ERROR: Rotor MIN >= MAX. This will explode the rotor. Please change. ";
                    } else {

                        lcdtxt2 += "\nROTOR: OK.  Min/max at " + MyRotor.LowerLimitDeg + " and " + MyRotor.UpperLimitDeg + " Speed settings (slow/fast): " + rotorspeed + "/" + rotorspeedfast;
                    }
                }
            } else {   // No rotor found !
                Echo("ERROR: no ROTOR found with name: " + DrillRotor);
                state = "error";
                lcdtxt2 += "\n ERROR: no ROTOR found with name: " + DrillRotor + "Solution: name your rotor correct or change the variable in the script to match your rotor name";
            }
            lcdtxt = "State :" + state + lcdtxt9;

            // Make the Drillgroup 
            if (GridTerminalSystem.GetBlockGroupWithName(Drillgroupname) != null) {
                blocksgroup = GridTerminalSystem.GetBlockGroupWithName(Drillgroupname);
                blocksgroup.GetBlocks(blocks);
                Drillgroup.Clear();
                foreach (var block in blocks) {

                    if (block is IMyShipDrill) { Drillgroup.Add((IMyShipDrill)block); }
                }
                Echo(Drillgroup.Count + " drills in group " + Drillgroupname);
                lcdtxt2 += "\nDRILLS: " + Drillgroup.Count + " drills found in group " + Drillgroupname;
            } else {
                Echo("Error, we did not find the Drill group: " + Drillgroupname);
                state = "error";
                lcdtxt2 += "\n DRILLS : Error, we did not find the Drill group: " + Drillgroupname + ". Solution: put your drills in a group with this name, or change the variable of the drill group in the script";
            }

            // Make the pistongroup1 
            if (GridTerminalSystem.GetBlockGroupWithName(Pistongroup1name) != null) {
                Pistongroup1.Clear();
                blocksgroup = GridTerminalSystem.GetBlockGroupWithName(Pistongroup1name);
                blocksgroup.GetBlocks(blocks);
                foreach (var block in blocks) {
                    if (block is IMyPistonBase) { Pistongroup1.Add((IMyPistonBase)block); }
                }
                Echo(Pistongroup1.Count + " pistons in group " + Pistongroup1name);
                lcdtxt2 += "\nPISTON GROUP 1: " + Pistongroup1.Count + " pistons in group " + Pistongroup1name + " set to extend by " + extendamountpiston1 + " each round";
            } else {
                Echo("Error, we did not find the piston group 1: " + Pistongroup1name);
                state = "error";
                lcdtxt2 += "\nPISTON GROUP 1: Error, we did not find the piston group 1: " + Pistongroup1name + ". Solution: put the pistons you want to use in a group with this name or change the name of the group in the script variable";
            }

            // Make the pistongroup2
            if (GridTerminalSystem.GetBlockGroupWithName(Pistongroup2name) != null) {
                Pistongroup2.Clear();
                blocksgroup = GridTerminalSystem.GetBlockGroupWithName(Pistongroup2name);
                blocksgroup.GetBlocks(blocks);
                foreach (var block in blocks) {
                    if (block is IMyPistonBase) { Pistongroup2.Add((IMyPistonBase)block); }
                }

                lcdtxt2 += "\nPISTON GROUP 2: " + Pistongroup2.Count + " pistons found in group " + Pistongroup2name + " set to extend by " + extendamountpiston2 + " each round";
            } else {

                lcdtxt2 += "\n PISTON GROUP 2:" + "Warning, NO piston group2 was found with name: " + Pistongroup2name + ". This could be OK if your drill does not have a second piston arm. In that case ignore";
            }
            lcdtxt2 += "\nAll pistons will extend at speed " + pistonspeed;

            // Make the CargoContainergroup
            if (GridTerminalSystem.GetBlockGroupWithName(Cargocontainergroupname) != null) {
                containermaxcap = 0;
                DrillContainers.Clear();
                blocksgroup = GridTerminalSystem.GetBlockGroupWithName(Cargocontainergroupname);
                blocksgroup.GetBlocks(blocks);
                foreach (var block in blocks) {
                    if (block is IMyCargoContainer) { DrillContainers.Add((IMyCargoContainer)block); containermaxcap = containermaxcap + Math.Round((float)block.GetInventory(0).MaxVolume); }
                }
                lcdtxt2 += "\nContainers : " + DrillContainers.Count + " found in group " + Cargocontainergroupname + "with max capacity of " + containermaxcap + ".000 l";
                if (Cargofillallowed == 100) { lcdtxt2 += "\nPause drilling to container capacity is OFF (max set to 100)"; } else { lcdtxt2 += "\nDrilling to be paused as of " + Cargofillallowed + " % filled"; }
            } else {
                containercheck = false;
                lcdtxt2 += "\n Containers: NO Containers within group: " + Cargocontainergroupname + " No check container capacity is done.";
            }
        }  // end of INIT  

        public void drillit() {
            for (int i = 0; i < Drillgroup.Count; i++) { Drillgroup[i].Enabled = true; };   //Start the drills
            waitfor = "360";
            versnelling();
            state = "drilling";
        }

        public void stop() {
            for (int i = 0; i < Drillgroup.Count; i++) { Drillgroup[i].Enabled = false; };   //Stop the drills
            MyRotor.TargetVelocityRPM = 0;  //stop the rotor
            for (int i = 0; i < Pistongroup1.Count(); i++)  //stop group 1 pistons
            { Pistongroup1[i].Velocity = 0; }
            for (int i = 0; i < Pistongroup2.Count(); i++)  //stop group 2 pistons
            { Pistongroup2[i].Velocity = 0; }
            state = "none";
        }

        public void extend(string group)  // should extend ThePiston.  Needs a group to determine speed and extention length
        {
            float extendby;
            if (group == "group1") { extendby = extendamountpiston1; } else { extendby = extendamountpiston2; }

            ThePiston = GridTerminalSystem.GetBlockWithId(extendingpiston) as IMyPistonBase;
            if (ThePiston.CurrentPosition > ThePiston.MaxLimit) { ThePiston.MaxLimit = ThePiston.CurrentPosition; } // fix a potential anomaly caused by user interference
            if ((ThePiston.MaxLimit + extendby) > ThePiston.HighestPosition) { ThePiston.MaxLimit = ThePiston.HighestPosition; } else { ThePiston.MaxLimit = ThePiston.MaxLimit + extendby; }

            ThePiston.Velocity = pistonspeed;
            ThePiston.Extend();
            extendtimer = 0;
            if (state != "kickstarting") { state = "extending"; }
        }

        public void extendsinglepiston(string group) {
            // Todo check status is none
            if (group == "group1") { FindPiston(Pistongroup1); } else { FindPiston(Pistongroup2); }   // finding the piston that can be moved based on the group asked
            if (!alldone) { nextaction = "none"; extend(group); }   // if alldone is false we found one and we can extend
        }

        public void retract(string group)  // should retract a group
        {
            switch (group) {
                case "group1":
                    for (int i = 0; i < Pistongroup1.Count(); i++) {
                        Pistongroup1[i].MinLimit = 0;
                        Pistongroup1[i].MaxLimit = 0;
                        Pistongroup1[i].Velocity = (float)-0.5;
                        Pistongroup1[i].Retract();
                    }
                    break;
                case "group2":
                    for (int i = 0; i < Pistongroup2.Count(); i++) {
                        Pistongroup2[i].MinLimit = 0;
                        Pistongroup2[i].MaxLimit = 0;
                        Pistongroup2[i].Velocity = (float)-0.5;
                        Pistongroup2[i].Retract();
                    }
                    break;
            }
            if (state != "none") { state = "retracting"; }  //Only if we are in running mode, we consider retracting
        }

        public bool retractsinglepiston(List<IMyPistonBase> group)  //tries to retract one piston 1m to recover a stuck drill arm
        {
            bool success = false;
            foreach (var block in group) {
                success = false;
                if (block.CurrentPosition != 0) {
                    success = true;
                    if (block.CurrentPosition - 1 < 0) { block.MinLimit = 0; } else { block.MinLimit = block.CurrentPosition - 1; }
                    block.MaxLimit = block.MinLimit;  //set the max to the min to adjust the new position for the next extend check.
                    block.Velocity = -pistonspeed;
                    break;
                }  //we found one that is not done.  Stop searching
            }
            if (success == true) { return true; } else { return false; }
        }

        public void dirtydrills() {
            drillsaredirty = false;

            for (int i = 0; i < Drillgroup.Count; i++) {
                if (Drillgroup[i].GetInventory(0).ItemCount > 0) { drillsaredirty = true; break; }

            };

        }

        public void versnelling() {
            double calc;
            double up, low, angle, delta;
            float finalspeed;
            angle = (Math.Round(MyRotor.Angle * (180 / Math.PI))) + 360;
            up = (Math.Round(MyRotor.UpperLimitRad * (180 / Math.PI))) + 360;
            low = (Math.Round(MyRotor.LowerLimitRad * (180 / Math.PI))) + 360;
            delta = ((up + low) / 2);

            if (rotorspeed != rotorspeedfast) {

                // Determine the potential extra speed
                if (angle <= (delta)) { calc = ((1 - ((delta - angle) / (delta - low))) * (rotorspeedfast - rotorspeed)); } else { calc = ((1 - ((angle - delta) / (up - delta))) * (rotorspeedfast - rotorspeed)); }

                // Determine if we use it
                dirtydrills();
                if (drillsaredirty == true) { finalspeed = rotorspeed; } else { finalspeed = (rotorspeed + (float)calc); }

                // Determine the direction, plus or minus" " 
                if (waitfor == "0") { finalspeed = 0 - finalspeed; }

                // Set the speed
                MyRotor.TargetVelocityRPM = finalspeed;

                lcdtxt3 = "Rotor speed " + meter(rotorspeed, rotorspeedfast, finalspeed, 10);

            }  //end of part when 2 different speeds are set
            else {
                // fast and slow speed are the same, so fixed and no meter to show for speed. 
                if (waitfor == "0") { MyRotor.TargetVelocityRPM = -rotorspeed; } else { MyRotor.TargetVelocityRPM = rotorspeed; }
                lcdtxt3 = "Rotor speed fixed at: " + rotorspeed;
            }
            lcdtxt3 += " Rotor position " + meter((low - 360), (up - 360), (angle - 360), 10);
        }

        public void updatelcd() {        // Lcdtxt is status info
                                         // Lcdtxt2 is available items info or error info
                                         // LCDtxt3 is the rotor info
                                         // Lcdtxt 5 and 6 are ThePiston info on group 1 and 2
                                         // LCDtxt4 is commands available
                                         // Lcdtxt7 is drills capacity info
                                         // LCDtxt8 is error on using extend while not in none state
                                         // lcdtext9 is info on recovery 
                                         // lcdtext10 is info on container capacity
                                         // lcdtxt11 is info on ismovementdone 


            string lcdmessage = "";
            lcdmessage += "Kristofs Lazy Drill script " + version + "\n\n" + lcdtxt;

            status(Pistongroup1, Pistongroup1name);
            status(Pistongroup2, Pistongroup2name);

            switch (state) {

                case "pausedrill":

                    if (lcdtxt7 != "") { lcdmessage += "\n\n" + lcdtxt7; }  // drills capacity
                    lcdmessage += "\n\nDrilling will be resumed if the drills \nare not filled 100% again. \nMaybe you need to use a sorter to remove ore from the drills faster ?";
                    break;

                case "pausecontainer":

                    if (lcdtxt10 != "") { lcdmessage += "\n" + lcdtxt10; } // Container capa
                    lcdmessage += "\n\nDrilling will be resumed automatic \nif the containers are below " + Cargofillallowed + "% again. \nMaybe you need to use a sorter to remove ore from the containers faster ? \n The max % can be set in the custom data of the PB";
                    break;

                case "none":

                    if (lcdtxt5 != "") { lcdmessage += "\n" + lcdtxt5; }  // pistons 1
                    if (lcdtxt6 != "") { lcdmessage += "\n" + lcdtxt6; }  // pistons 2
                    if (lcdtxt4 != "") { lcdmessage += "\n" + lcdtxt4; }  // commands
                    if (lcdtxt7 != "") { lcdmessage += "\n\n" + lcdtxt7; }  // drills capacity
                    if (lcdtxt2 != "") { lcdmessage += "\n" + lcdtxt2; } // info on elements ? 
                    break;

                case "error":

                    if (lcdtxt4 != "") { lcdmessage += "\n" + lcdtxt4; }  // commands
                    if (lcdtxt7 != "") { lcdmessage += "\n\n" + lcdtxt7; }  // drills capacity
                    if (lcdtxt2 != "") { lcdmessage += "\n" + lcdtxt2; } // error info
                    break;

                default:

                    if (lcdtxt8 != "") { lcdmessage += "\n" + lcdtxt8 + "\n"; }  // error on extend command in wrong state
                    if (lcdtxt3 != "") { lcdmessage += "\n" + lcdtxt3; }  // rotor
                    if (lcdtxt5 != "") { lcdmessage += "\n" + lcdtxt5; }  // pistons 1
                    if (lcdtxt6 != "") { lcdmessage += "\n" + lcdtxt6; }  // pistons 2
                    if (lcdtxt7 != "") { lcdmessage += "\n" + lcdtxt7; } // Drill capa
                    if (lcdtxt10 != "") { lcdmessage += "\n" + lcdtxt10; } // Container capa
                    if (lcdtxt4 != "") { lcdmessage += "\n\n" + lcdtxt4; }   // commands
                    //if (lcdtxt2 != "") { lcdmessage += "\n" + lcdtxt2; }  // info ? 
                    break;

            }
            textSurface.WriteText(lcdmessage);


        }


        public void pistoncheck() {  // This code sets all pistons to have min and max set to 0 and speed 0
            for (int i = 0; i < Pistongroup1.Count(); i++) {
                Pistongroup1[i].MinLimit = 0;
                Pistongroup1[i].MaxLimit = 0;
                Pistongroup1[i].Velocity = (float)-2;
            }

            for (int i = 0; i < Pistongroup2.Count(); i++) {
                Pistongroup2[i].MinLimit = 0;
                Pistongroup2[i].MaxLimit = 0;
                Pistongroup2[i].Velocity = (float)-2;

            }
        }

        public void MakeIni() {
            _ini.Clear();
            _ini.Set("Lazydrill_Settings", "DrillRotor", DrillRotor);
            _ini.Set("Lazydrill_Settings", "LCD_Name", LCD_Name);
            _ini.Set("Lazydrill_Settings", "Pistongroup1name", Pistongroup1name);
            _ini.Set("Lazydrill_Settings", "Pistongroup2name", Pistongroup2name);
            _ini.Set("Lazydrill_Settings", "Drillgroupname", Drillgroupname);
            _ini.Set("Lazydrill_Settings", "rotorspeed", rotorspeed);
            _ini.Set("Lazydrill_Settings", "rotorspeedfast", rotorspeedfast);
            _ini.Set("Lazydrill_Settings", "extendamountpiston1", extendamountpiston1);
            _ini.Set("Lazydrill_Settings", "extendamountpiston2", extendamountpiston2);
            _ini.Set("Lazydrill_Settings", "pistonspeed", pistonspeed);
            _ini.Set("Lazydrill_Settings", "Cargocontainergroupname", Cargocontainergroupname);
            _ini.Set("Lazydrill_Settings", "Cargofillallowed", Cargofillallowed);
            _ini.Set("Lazydrill_Info", "Info V1_4", "These variables are set first by the scripts default. \n You can CHANGE the values here afterwards.  \n The script will use your values instead of the defaults then. \n Use STOP before changing them, then use START or RESET after changing");
            Me.CustomData = "";
            Me.CustomData = _ini.ToString();
        }  //end of Makeini


        public void ReadIni() {
            bool iniupdateneeded = false;  //Do we need to write a new version of the INI ? 
            MyIniParseResult result;
            string mystring;
            float myfloat;
            string myvariable;  //the variable we are looking for

            if (!_ini.TryParse(Me.CustomData, out result)) //No INI found ? 
            {
                Echo("Custom Data was filled with script variables");
                MakeIni();
            } else // There is an INI present
              {   // DrillRotor
                myvariable = "Drillrotor";
                mystring = _ini.Get("Lazydrill_Settings", myvariable).ToString();
                if (mystring != "") {
                    Echo(myvariable + " found: " + mystring);
                    DrillRotor = _ini.Get("Lazydrill_Settings", myvariable).ToString();
                } else { Echo(myvariable + " value not found, set to default value of script"); iniupdateneeded = true; }

                // DrillGroup
                myvariable = "Drillgroupname";
                mystring = _ini.Get("Lazydrill_Settings", myvariable).ToString();
                if (mystring != "") {
                    Echo(myvariable + " found: " + mystring);
                    Drillgroupname = _ini.Get("Lazydrill_Settings", myvariable).ToString();
                } else { Echo(myvariable + " value not found, set to default value of script"); iniupdateneeded = true; }

                //Pistongroup1
                myvariable = "Pistongroup1name";
                mystring = _ini.Get("Lazydrill_Settings", myvariable).ToString();
                if (mystring != "") {
                    Echo(myvariable + " found: " + mystring);
                    Pistongroup1name = _ini.Get("Lazydrill_Settings", myvariable).ToString();
                } else { Echo(myvariable + " value not found, set to default value of script"); iniupdateneeded = true; }

                //Pistongroup2
                myvariable = "Pistongroup2name";
                mystring = _ini.Get("Lazydrill_Settings", myvariable).ToString();
                if (mystring != "") {
                    Echo(myvariable + " found: " + mystring);
                    Pistongroup2name = _ini.Get("Lazydrill_Settings", myvariable).ToString();
                } else { Echo(myvariable + " value not found, set to default value of script"); iniupdateneeded = true; }

                //LCD_Name
                myvariable = "LCD_Name";
                mystring = _ini.Get("Lazydrill_Settings", myvariable).ToString();
                if (mystring != "") {
                    Echo(myvariable + " found: " + mystring);
                    LCD_Name = _ini.Get("Lazydrill_Settings", myvariable).ToString();
                } else { Echo(myvariable + " value not found, set to default value of script"); iniupdateneeded = true; }

                //rotorspeed
                myvariable = "rotorspeed";
                myfloat = _ini.Get("Lazydrill_Settings", myvariable).ToSingle();
                if (myfloat != 0) {
                    Echo(myvariable + " found: " + myfloat);
                    rotorspeed = _ini.Get("Lazydrill_Settings", myvariable).ToSingle();
                } else { Echo(myvariable + " value not found, set to default value of script"); iniupdateneeded = true; }

                //rotorspeedfast
                myvariable = "rotorspeedfast";
                myfloat = _ini.Get("Lazydrill_Settings", myvariable).ToSingle();
                if (myfloat != 0) {
                    Echo(myvariable + " found: " + myfloat);
                    rotorspeedfast = _ini.Get("Lazydrill_Settings", myvariable).ToSingle();
                } else { Echo(myvariable + " value not found, set to default value of script"); iniupdateneeded = true; }

                //extendamountpiston1
                myvariable = "extendamountpiston1";
                myfloat = _ini.Get("Lazydrill_Settings", myvariable).ToSingle();
                if (myfloat != 0) {
                    Echo(myvariable + " found: " + myfloat);
                    extendamountpiston1 = _ini.Get("Lazydrill_Settings", myvariable).ToSingle();
                } else { Echo(myvariable + " value not found, set to default value of script"); iniupdateneeded = true; }

                //extendamountpiston2
                myvariable = "extendamountpiston2";
                myfloat = _ini.Get("Lazydrill_Settings", myvariable).ToSingle();
                if (myfloat != 0) {
                    Echo(myvariable + " found: " + myfloat);
                    extendamountpiston2 = _ini.Get("Lazydrill_Settings", myvariable).ToSingle();
                } else { Echo(myvariable + " value not found, set to default value of script"); iniupdateneeded = true; }

                //pistonspeed
                myvariable = "pistonspeed";
                myfloat = _ini.Get("Lazydrill_Settings", myvariable).ToSingle();
                if (myfloat != 0) {
                    Echo(myvariable + " found: " + myfloat);
                    pistonspeed = _ini.Get("Lazydrill_Settings", myvariable).ToSingle();
                } else { Echo(myvariable + " value not found, set to default value of script"); iniupdateneeded = true; }


                //Cargocontainergroupname
                myvariable = "Cargocontainergroupname";
                mystring = _ini.Get("Lazydrill_Settings", myvariable).ToString();
                if (mystring != "") {
                    Echo(myvariable + " found: " + mystring);
                    Cargocontainergroupname = _ini.Get("Lazydrill_Settings", myvariable).ToString();
                } else { Echo(myvariable + " value not found, set to default value of script"); iniupdateneeded = true; }

                //Cargofillallowed
                myvariable = "Cargofillallowed";

                myfloat = _ini.Get("Lazydrill_Settings", myvariable).ToSingle();

                if (_ini.Get("Lazydrill_Settings", myvariable).ToString() != "") {
                    Echo(myvariable + " found: " + myfloat);
                    Cargofillallowed = _ini.Get("Lazydrill_Settings", myvariable).ToSingle();
                } else { Echo(myvariable + " value not found, set to default value of script"); iniupdateneeded = true; }


                // check if we need to make a new one. 

                if (iniupdateneeded) { MakeIni(); Echo("Custom Data was UPDATED as some script variables where missing"); }  //it seems we need to write a new CustomData

            }
        } //end of readini

        public void FindPiston(List<IMyPistonBase> group) {
            foreach (var block in group) {
                alldone = true;
                if (block.CurrentPosition != block.HighestPosition) {
                    alldone = false;
                    extendingpiston = block.EntityId;
                    break;
                }  //we found one that is not done.  Stop searching
            }
        }   // end findpiston

        public void status(List<IMyPistonBase> pistongroup, string groupname) {
            float maxtodo = 0;
            float done = 0;
            string graph = "";

            foreach (var block in pistongroup) {
                maxtodo = maxtodo + block.HighestPosition;
                done = done + block.CurrentPosition;

                graph += "[" + Math.Round(block.CurrentPosition, 1) + "] ";
            }
            if (groupname == Pistongroup1name) { lcdtxt5 = "Piston group (" + groupname + ") at " + Math.Round(done, 1) + "m of " + maxtodo + "m " + graph; }
            if (groupname == Pistongroup2name) { lcdtxt6 = "Piston group (" + groupname + ") at " + Math.Round(done, 1) + "m of " + maxtodo + "m " + graph; }

        }


        public int Drillfill() {
            double usedcap = 0;
            double maxcap = 0;
            int usage = 0;
            foreach (var block in Drillgroup) {
                usedcap = usedcap + (float)block.GetInventory(0).CurrentVolume;
                maxcap = maxcap + (float)block.GetInventory(0).MaxVolume;

            }
            usage = (int)Math.Round((usedcap / maxcap) * 100);

            lcdtxt7 = "Drills inventory usage %: " + meter(0, 100, usage, 10);
            return usage;

        }


        public string meter(double min, double max, double current, int parts) {  // returns a meter that shows min and max with 'parts' number of symbols showing current position
            string output = min + " [";
            if (current < 0) { current = -current; }
            // alt 16 ►  alt 17 ◄  alt 177 ▒  alt 176 ░  219 █    ▓▓▓░░   ►►►--
            double element = (max - min) / parts;  //how much speed is one part
            int reached = (int)Math.Round(((current - min) / element));// how much elements do we reach

            for (int i = 1; i <= reached; i++) { output += "-"; }
            output += Math.Round(current, 1);
            for (int i = (parts - reached); i > 0; i--) { output += "-"; }
            output += "] " + max;
            return output;
        }

        public double progress() {
            double done = 0;
            foreach (var block in Pistongroup1) {
                done = done + block.CurrentPosition;

            }
            return done;
        }

        public int Containerfill() {
            double usedcap = 0;
            int usage = 0;
            foreach (var block in DrillContainers) {
                usedcap = usedcap + (float)block.GetInventory(0).CurrentVolume;
            }
            usage = (int)Math.Round((usedcap / containermaxcap) * 100);

            if (Cargofillallowed != 100) {
                lcdtxt10 = "Container inventory usage %: " + meter(0, Cargofillallowed, usage, 10) + "  " + Math.Round(usedcap) + ".000 liters used";
            } else { lcdtxt10 = "Container inventory usage %: " + meter(0, 100, usage, 10) + "  " + Math.Round(usedcap) + ".000 liters used, no max set"; }
            return usage;
        }

        //   end of code 
    }
}
