namespace CustomProfiler.Patches.Optimizations;

using HarmonyLib;
using PlayerRoles.FirstPersonControl;
using PlayerStatsSystem;
using RelativePositioning;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static HarmonyLib.AccessTools;

[HarmonyPatch]
public static class FirstPersonMovementModulePatch
{
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(FirstPersonMovementModule), nameof(FirstPersonMovementModule.UpdateMovement))]
    private static IEnumerable<CodeInstruction> FirstPersonMovementModule_UpdateMovement_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        return new CodeInstruction[]
        {
            new(OpCodes.Ldarg_0),
            new(OpCodes.Call, Method(typeof(FirstPersonMovementModulePatch), nameof(Custom_FirstPersonMovementModule_UpdateMovement))),
            new(OpCodes.Ret),
        };
    }

    private static void Custom_FirstPersonMovementModule_UpdateMovement(FirstPersonMovementModule _this)
    {
        _this._speedState = _this.SyncMovementState = Custom_FpcStateProcessor_UpdateMovementState(_this.StateProcessor, _this.CurrentMovementState);
        Custom_FpcMotor_UpdatePosition(_this.Motor);
        Custom_FpcNoclip_UpdateNoclip(_this.Noclip);
        Custom_FpcMouseLook_UpdateRotation(_this.MouseLook);
    }

    private static PlayerMovementState Custom_FpcStateProcessor_UpdateMovementState(FpcStateProcessor _this, PlayerMovementState state)
    {
        if (_this._useRate == 0f)
            return state;

        if (_this._firstUpdate)
        {
            _this._firstUpdate = false;

            _this._mod.CharController.height = _this._mod.CharacterControllerSettings.Height;
            _this._mod.CharController.center = Vector3.zero;
        }

        if (state == PlayerMovementState.Sprinting)
        {
            if (_this._stat.CurValue > 0f && !_this.SprintingDisabled)
            {
                _this._stat.CurValue = Mathf.Clamp01(_this._stat.CurValue - (Time.deltaTime * _this.ServerUseRate));
                _this._regenStopwatch.Restart();
                return PlayerMovementState.Sprinting;
            }

            state = PlayerMovementState.Walking;
        }

        if (_this._stat.CurValue < 1f)
        {
            _this._stat.CurValue = Mathf.Clamp01(_this._stat.CurValue + (_this.ServerRegenRate * Time.deltaTime));
        }

        return state;
    }

    // this is the most intense due to CharacterController.Move
    private static void Custom_FpcMotor_UpdatePosition(FpcMotor _this)
    {
        _this._lastMaxSpeed = _this.Speed;

        if (_this.MainModule.Noclip.IsActive)
        {
            _this.MoveDirection = Vector3.zero;
            return;
        }

        _this.MoveDirection = new Vector3(_this.DesiredMove.x * _this._lastMaxSpeed, _this.MoveDirection.y, _this.DesiredMove.z * _this._lastMaxSpeed);

        if (_this.MainModule.CharController.isGrounded)
        {
            Custom_Update_Grounded(_this, _this.MainModule.JumpSpeed);
        }
        else
        {
            _this.UpdateFloating();
        }

        static void Custom_Update_Grounded(FpcMotor _this, float jumpSpeed)
        {
            Vector3 moveDirection = _this.MoveDirection;
            bool isFloating = false;

            if (_this.IsJumping = _this.WantsToJump)
            {
                if (jumpSpeed > 0f)
                {
                    moveDirection.y = jumpSpeed;
                    isFloating = true;
                }
                _this._requestedJump = false;
            }
            else
            {
                moveDirection.y = -10f;
            }

            _this.MoveDirection = moveDirection;

            if (_this._maxFallSpeed > 14.5f && _this._enableFallDamage)
            {
                _this.ServerProcessFall(_this._maxFallSpeed - 14.5f);
            }

            _this._maxFallSpeed = 14.5f;

            if (isFloating)
            {
                _this.UpdateFloating();
                return;
            }

            // If we arent moving very far, then we dont waste a call
            // on CharacterController.Move, which is intense
            if (Math.Abs(moveDirection.x) < Mathf.Epsilon
                && moveDirection.y == -10f
                && Math.Abs(moveDirection.z) < Mathf.Epsilon)
                return;

            _this.Move();

            if (!_this.MainModule.CharController.isGrounded)
            {
                _this.MoveDirection = Vector3.Scale(_this.MoveDirection, new Vector3(1f, 0f, 1f));
            }
        }
    }

    private static void Custom_FpcNoclip_UpdateNoclip(FpcNoclip _this)
    {
        if ((_this._stats.Flags & AdminFlags.Noclip) == 0)
            return;

        _this._lastNcSw.Restart();
        _this._fpmm.Motor._fallDamageImmunity.Restart();
        _this._fpmm.Position = _this._fpmm.Motor.ReceivedPosition.Position;
    }

    private static void Custom_FpcMouseLook_UpdateRotation(FpcMouseLook _this)
    {
        _this.CurrentHorizontal = WaypointBase.GetWorldRotation(_this._fpmm.Motor.ReceivedPosition.WaypointId, Quaternion.Euler(Vector3.up * _this._syncHorizontal)).eulerAngles.y;
        _this.CurrentVertical = _this._syncVertical;

        _this._hub.transform.rotation = _this.TargetHubRotation;
        _this._hub.PlayerCameraReference.localRotation = _this.TargetCamRotation;
    }
}
