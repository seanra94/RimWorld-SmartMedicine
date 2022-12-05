using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;
using HarmonyLib;

namespace SmartMedicine
{
	[DefOf]
	public static class SmartMedicineJobDefOf
	{
		public static JobDef StockUp;
		public static JobDef StockDown;
	}

	public class JobGiver_StockUp : ThinkNode_JobGiver
	{

		public static Dictionary<Pawn, int> lastStockCheck = new Dictionary<Pawn, int>();
		public static bool Skip(Pawn pawn)
		{
			if (pawn.inventory.UnloadEverything)
				return true;

			Log.Message($"Skip need tend?");
			if (pawn.Map.mapPawns.AllPawnsSpawned.Any(p => HealthAIUtility.ShouldBeTendedNowByPlayer(p) && pawn.CanReserveAndReach(p, PathEndMode.ClosestTouch, Danger.Deadly, ignoreOtherReservations: true)))
				return true;

			if (pawn.Map.mapPawns.AllPawnsSpawned.Any(p => p is IBillGiver billGiver && billGiver.BillStack.AnyShouldDoNow && pawn.CanReserveAndReach(p, PathEndMode.ClosestTouch, Danger.Deadly, ignoreOtherReservations: true)))
				return true;

			return false;
		}
		protected override Job TryGiveJob(Pawn pawn)
		{
			if (pawn.StockUpIsFull()) return null;

			int currentHour = GenLocalDate.HourOfDay(pawn.Map);
			try
			{
				if (lastStockCheck[pawn] != currentHour)
				{
					lastStockCheck[pawn] = currentHour;
				}
				else
				{
					return null;
				}
			}
			catch (KeyNotFoundException)
			{
				Log.Message($"Adding {pawn} to dictionary");
				lastStockCheck.Add(pawn, currentHour);
			}
			
			
			if (Skip(pawn))
				return null;

			Log.Message($"{GenLocalDate.HourOfDay(pawn.Map)}: {pawn} needs stocking up");

			Log.Message($"Checking if any stock available for {pawn}");
			Predicate<Thing> validator = (Thing t) => pawn.StockingUpOn(t) && pawn.StockUpNeeds(t) > 0 && pawn.CanReserve(t, FindBestMedicine.maxPawns, 1) && !t.IsForbidden(pawn);
			Thing thing = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForGroup(ThingRequestGroup.HaulableEver), PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999, validator);
			if (thing != null)
			{
				int pickupCount = Math.Min(pawn.StockUpNeeds(thing), MassUtility.CountToPickUpUntilOverEncumbered(pawn, thing));
				if (pickupCount > 0)
					Log.Message($"{pawn} stocking up on {pickupCount} {thing}");
				return new Job(SmartMedicineJobDefOf.StockUp, thing) { count = pickupCount};
			}

			
			Thing toReturn = pawn.StockUpThingToReturn();


			if (toReturn == null)
			{
				Log.Message($"{pawn} has nothing to return");
				return null;
			}
				
			Log.Message($"{pawn} looking to return {toReturn.LabelShort}");

			int dropCount = -pawn.StockUpNeeds(toReturn);
			Log.Message($"{pawn} dropping {dropCount} {toReturn.LabelShort}");
			if (StoreUtility.TryFindBestBetterStoreCellFor(toReturn, pawn, pawn.Map, StoragePriority.Unstored, pawn.Faction, out IntVec3 dropLoc, true))
				return new Job(SmartMedicineJobDefOf.StockDown, toReturn, dropLoc) { count = dropCount };
			Log.Message($"{pawn} has nowhere to store {toReturn.LabelShort}");
			return null;
		}
	}

	//private void CleanupCurrentJob(JobCondition condition, bool releaseReservations, bool cancelBusyStancesSoft = true)
	[HarmonyPatch(typeof(Pawn_JobTracker), "CleanupCurrentJob")]
	public static class CleanupCurrentJob_Patch
	{
		public static void Prefix(Pawn_JobTracker __instance, Pawn ___pawn)
		{
			if (__instance.curJob?.def == JobDefOf.TendPatient)
			{
				Pawn pawn = ___pawn;
				if (!pawn.Destroyed && pawn.carryTracker != null && pawn.carryTracker.CarriedThing != null)
				{
					if (StockUpUtility.StockingUpOn(pawn, pawn.carryTracker.CarriedThing))
						pawn.inventory.innerContainer.TryAddOrTransfer(pawn.carryTracker.CarriedThing);
				}
			}
		}
	}
}