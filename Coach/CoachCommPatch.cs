using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;

namespace MatchZy;

// matchzy_coach_comm_unrestricted: CS:GO's sv_coach_comm_unrestricted for CS2.
//
// CS2 (RE of build 14196, both platforms) routes a coach's voice AND say_team through one gamerules
// check: the coach (sv_coaching_enabled 1, controller on Spectator, m_iCoachingTeam 2/3) reaches its
// coached team only during warmup, a T/CT tactical timeout or halftime. CS:GO had a cvar to lift
// that; CS2 removed the cvar but kept the gate. The gamedata key
// "CCSGameRules_CanPlayerHear_CoachCommGate" points at the conditional jump that sends "warmup"
// to "return true". Making that jump unconditional makes coach -> coached team always true. The
// team check before it is untouched, so the enemy still never hears or reads the coach.
//
// The byte signature lives only in gamedata/matchzy.json. The patch bytes are chosen from what is
// found at the site: linux "0F 85 rel32" (jnz) -> "90 E9" (nop; jmp rel32), windows "75 rel8" (jnz)
// -> "EB" (jmp rel8). The displacement after them is kept, so the jump target does not change.
// Anything else at the site (a new build moved the code) is left alone and logged.
public partial class MatchZy
{
    private nint _coachCommSite;
    private byte[]? _coachCommOriginal;
    private bool _coachCommGaveUp;

    [DllImport("libc.so.6", EntryPoint = "mprotect", SetLastError = true)]
    private static extern int CoachCommMprotect(nint addr, nuint len, int prot);

    [DllImport("kernel32", EntryPoint = "VirtualProtect", SetLastError = true)]
    private static extern bool CoachCommVirtualProtect(nint addr, nuint size, uint newProt, out uint oldProt);

    private void RegisterCoachCommPatch()
    {
        // Applied on change (config.cfg runs after Load) and taken back on 0 / Unload.
        coachCommUnrestricted.ValueChanged += (_, on) => Server.NextFrame(() => SetCoachCommPatch(on));
        if (coachCommUnrestricted.Value)
            SetCoachCommPatch(true);
    }

    private void SetCoachCommPatch(bool on)
    {
        try
        {
            if (on) EnableCoachCommPatch();
            else DisableCoachCommPatch("matchzy_coach_comm_unrestricted 0");
        }
        catch (Exception e)
        {
            Log($"[CoachCommPatch] {(on ? "enable" : "disable")} failed: {e.GetType().Name}: {e.Message}");
        }
    }

    private void EnableCoachCommPatch()
    {
        if (_coachCommOriginal != null)
            return;
        if (_coachCommGaveUp)
        {
            Log("[CoachCommPatch] not patching: the gate was not usable on this build (see the earlier log line).");
            return;
        }

        string sig;
        try
        {
            sig = MzGameData.GetSignature("CCSGameRules_CanPlayerHear_CoachCommGate");
        }
        catch (Exception e)
        {
            _coachCommGaveUp = true;
            Log($"[CoachCommPatch] gamedata key CCSGameRules_CanPlayerHear_CoachCommGate missing ({e.Message}); coach voice/chat stays restricted. Deploy gamedata/matchzy.json for this CS2 build.");
            return;
        }

        nint site = NativeAPI.FindSignature(Addresses.ServerPath, sig);
        if (site == 0)
        {
            _coachCommGaveUp = true;
            Log("[CoachCommPatch] coach comm gate signature not found in the server binary; coach voice/chat stays restricted.");
            return;
        }

        byte b0 = Marshal.ReadByte(site);
        byte b1 = Marshal.ReadByte(site + 1);
        byte[] original, patch;
        string kind;
        if (b0 == 0x0F && b1 == 0x85)
        {
            original = new[] { b0, b1 };
            patch = new byte[] { 0x90, 0xE9 };
            kind = "jnz rel32 -> nop; jmp rel32";
        }
        else if (b0 == 0x75)
        {
            original = new[] { b0 };
            patch = new byte[] { 0xEB };
            kind = "jnz rel8 -> jmp rel8";
        }
        else
        {
            _coachCommGaveUp = true;
            Log($"[CoachCommPatch] unexpected bytes {b0:X2} {b1:X2} at the coach comm gate (0x{site:X}); not patching, coach voice/chat stays restricted.");
            return;
        }

        if (!WriteCoachCommBytes(site, patch))
            return;
        _coachCommSite = site;
        _coachCommOriginal = original;
        Log($"[CoachCommPatch] coach voice + team chat unrestricted: patched 0x{site:X} ({kind}, was {BitConverter.ToString(original).Replace('-', ' ')}).");
    }

    private void DisableCoachCommPatch(string reason)
    {
        if (_coachCommOriginal == null || _coachCommSite == 0)
            return;
        if (WriteCoachCommBytes(_coachCommSite, _coachCommOriginal))
            Log($"[CoachCommPatch] original bytes restored at 0x{_coachCommSite:X} ({reason}); coach voice/chat restricted again.");
        _coachCommOriginal = null;
        _coachCommSite = 0;
    }

    private bool WriteCoachCommBytes(nint at, byte[] bytes)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!CoachCommVirtualProtect(at, (nuint)bytes.Length, 0x40 /* PAGE_EXECUTE_READWRITE */, out uint old))
            {
                Log($"[CoachCommPatch] VirtualProtect failed (error {Marshal.GetLastWin32Error()}); nothing written.");
                return false;
            }
            Marshal.Copy(bytes, 0, at, bytes.Length);
            CoachCommVirtualProtect(at, (nuint)bytes.Length, old, out _);
            return true;
        }

        nint page = at & ~(nint)0xFFF;
        nuint len = (nuint)(at + bytes.Length - page);
        if (CoachCommMprotect(page, len, 7 /* R|W|X */) != 0)
        {
            Log($"[CoachCommPatch] mprotect failed (errno {Marshal.GetLastWin32Error()}); nothing written.");
            return false;
        }
        Marshal.Copy(bytes, 0, at, bytes.Length);
        // .text of libserver.so is mapped R|X.
        CoachCommMprotect(page, len, 5 /* R|X */);
        return true;
    }
}
