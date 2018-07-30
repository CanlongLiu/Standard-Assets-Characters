﻿using System;
using Attributes;
using Attributes.Types;
using StandardAssets.Characters.CharacterInput;
using StandardAssets.Characters.Physics;
using UnityEngine;
using Util;

namespace StandardAssets.Characters.ThirdPerson
{
	[Serializable]
	public class RootMotionThirdPersonMotor : IThirdPersonMotor
	{		
		//Events
		public event Action startActionMode, startStrafeMode;

		//Serialized Fields
		[HelperBox(HelperType.Info, "Configuration is a separate asset. Click on the associated configuration to located it in the Project View. Values can be edited here during runtime and not be lost. It also allows one to create different settings and swap between them. To create a new setting Right click -> Create -> Standard Assets -> Characters -> Third Person Root Motion Configuration")]
		[SerializeField]
		protected ThirdPersonRootMotionConfiguration configuration;

		[SerializeField]
		protected Transform cameraTransform;

		[SerializeField]
		protected InputResponse runInput;

		[SerializeField]
		protected InputResponse strafeInput;

		//Properties
		public float normalizedTurningSpeed { get; private set; }
		public float normalizedLateralSpeed { get; private set; }
		public float normalizedForwardSpeed { get; private set; }

		public float fallTime
		{
			get { return characterPhysics.fallTime; }
		}

		public float targetYRotation { get; private set; }

		public Action jumpStarted { get; set; }
		public Action landed { get; set; }
		public Action<float> fallStarted { get; set; }
		public Action<float> rapidlyTurned { get; set; }

		//Protected fields

		/// <summary>
		/// The input implementation
		/// </summary>
		protected ICharacterInput characterInput;

		/// <summary>
		/// The physic implementation
		/// </summary>
		protected ICharacterPhysics characterPhysics;

		protected ThirdPersonAnimationController animationController;

		protected Animator animator;

		protected ThirdPersonMotorMovementMode movementMode = ThirdPersonMotorMovementMode.Action;

		protected ThirdPersonGroundMovementState preTurnMovementState;
		protected ThirdPersonGroundMovementState movementState = ThirdPersonGroundMovementState.Walking;

		protected ThirdPersonAerialMovementState aerialState = ThirdPersonAerialMovementState.Grounded;

		protected AnimationInputProperties currentForwardInputProperties, currentLateralInputProperties;

		protected float forwardClampSpeed, targetForwardClampSpeed, lateralClampSpeed, targetLateralClampSpeed;

		protected float cachedForwardMovement, averageForwardMovement;

		protected TurnaroundBehaviour turnaroundBehaviour;

		protected Transform transform;
		protected GameObject gameObject;

		protected ThirdPersonBrain thirdPersonBrain;

		protected bool isStrafing
		{
			get { return movementMode == ThirdPersonMotorMovementMode.Strafe; }
		}

		public float normalizedVerticalSpeed
		{
			get { return characterPhysics.normalizedVerticalSpeed; }
		}

		public ThirdPersonRootMotionConfiguration thirdPersonConfiguration
		{
			get { return configuration; }
		}

		public void OnJumpAnimationComplete()
		{
			var baseCharacterPhysics = characterPhysics as BaseCharacterPhysics;
			if (baseCharacterPhysics == null)
			{
				return;
			}

			var distance = baseCharacterPhysics.GetPredicitedFallDistance();
			if (distance <= configuration.maxFallDistanceToLand)
			{
				OnLanding();
			}
			else
			{
				aerialState = ThirdPersonAerialMovementState.Falling;
				if (fallStarted != null)
				{
					fallStarted(distance);
				}
			}
		}

		//Unity Messages
		public void OnAnimatorMove()
		{
			
			if (movementState == ThirdPersonGroundMovementState.TurningAround)
			{
				return;
			}

			if (ShouldApplyRootMotion())
			{
				Vector3 groundMovementVector = animator.deltaPosition * configuration.scaleRootMovement;
				groundMovementVector.y = 0;
				characterPhysics.Move(groundMovementVector);
			}
			else
			{
				characterPhysics.Move(cachedForwardMovement * transform.forward * configuration.scaledGroundVelocity);
			}
		}

		private bool ShouldApplyRootMotion()
		{
			return characterPhysics.isGrounded && animationController.shouldUseRootMotion;
		}

		public void Init(ThirdPersonBrain brain)
		{
			gameObject = brain.gameObject;
			transform = brain.transform;
			thirdPersonBrain = brain;
			turnaroundBehaviour = brain.turnaroundBehaviour;

			characterInput = brain.inputForCharacter;
			characterPhysics = brain.physicsForCharacter;
			animator = gameObject.GetComponent<Animator>();
			animationController = brain.animationControl;

			if (cameraTransform == null)
			{
				cameraTransform = Camera.main.transform;
			}

			if (runInput != null)
			{
				runInput.Init();
			}

			if (strafeInput != null)
			{
				strafeInput.Init();
			}

			OnStrafeEnded();

			currentForwardInputProperties = configuration.forwardMovementProperties;
			targetForwardClampSpeed = forwardClampSpeed = currentForwardInputProperties.inputClamped;
		}

		/// <summary>
		/// Subscribe
		/// </summary>
		public void Subscribe()
		{
			//Physics subscriptions
			characterPhysics.landed += OnLanding;
			characterPhysics.startedFalling += OnStartedFalling;

			//Input subscriptions
			characterInput.jumpPressed += OnJumpPressed;
			if (runInput != null)
			{
				runInput.started += OnRunStarted;
				runInput.ended += OnRunEnded;
			}

			if (strafeInput != null)
			{
				strafeInput.started += OnStrafeStarted;
				strafeInput.ended += OnStrafeEnded;
			}

			//Turnaround Subscription
			if (turnaroundBehaviour != null)
			{
				turnaroundBehaviour.turnaroundComplete += TurnaroundComplete;
			}
		}

		/// <summary>
		/// Unsubscribe
		/// </summary>
		public void Unsubscribe()
		{
			//Physics subscriptions
			if (characterPhysics != null)
			{
				characterPhysics.landed -= OnLanding;
				characterPhysics.startedFalling -= OnStartedFalling;
			}

			//Input subscriptions
			if (characterInput != null)
			{
				characterInput.jumpPressed -= OnJumpPressed;
			}

			if (runInput != null)
			{
				runInput.started -= OnRunStarted;
				runInput.ended -= OnRunEnded;
			}

			if (strafeInput != null)
			{
				strafeInput.started -= OnStrafeStarted;
				strafeInput.ended -= OnStrafeEnded;
			}

			//Turnaround Subscription
			if (turnaroundBehaviour != null)
			{
				turnaroundBehaviour.turnaroundComplete += TurnaroundComplete;
			}
		}

		public void Update()
		{
			HandleMovement();
			HandleClampSpeedDeceleration();
		}

		//Protected Methods
		/// <summary>
		/// Handles player landing
		/// </summary>
		protected virtual void OnLanding()
		{
			aerialState = ThirdPersonAerialMovementState.Grounded;
			//cachedForwardMovement = 0f;

			if (landed != null)
			{
				landed();
			}
		}

		/// <summary>
		/// Handles player falling
		/// </summary>
		/// <param name="predictedFallDistance"></param>
		protected virtual void OnStartedFalling(float predictedFallDistance)
		{
			if (aerialState == ThirdPersonAerialMovementState.Grounded)
			{
				cachedForwardMovement = averageForwardMovement;
			}

			aerialState = ThirdPersonAerialMovementState.Falling;

			if (fallStarted != null)
			{
				fallStarted(predictedFallDistance);
			}
		}

		/// <summary>
		/// Subscribes to the Jump action on input
		/// </summary>
		protected virtual void OnJumpPressed()
		{
			if (!characterPhysics.isGrounded || !animationController.shouldUseRootMotion)
			{
				return;
			}

			aerialState = ThirdPersonAerialMovementState.Jumping;
			
			if (jumpStarted != null)
			{
				jumpStarted();
			}

			if (Mathf.Abs(normalizedLateralSpeed) < normalizedForwardSpeed)
			{
				characterPhysics.SetJumpVelocity(configuration.initialJumpVelocity);
				cachedForwardMovement = averageForwardMovement;
			}
		}

		/// <summary>
		/// Method called by run input started
		/// </summary>
		protected virtual void OnRunEnded()
		{
			movementState = ThirdPersonGroundMovementState.Walking;
			targetForwardClampSpeed = currentForwardInputProperties.inputClamped;
			if (isStrafing)
			{
				targetLateralClampSpeed = currentLateralInputProperties.inputClamped;
			}
		}

		/// <summary>
		/// Method called by run input ended
		/// </summary>
		protected virtual void OnRunStarted()
		{
			movementState = ThirdPersonGroundMovementState.Running;
			targetForwardClampSpeed = currentForwardInputProperties.inputUnclamped;
			if (isStrafing)
			{
				targetLateralClampSpeed = currentLateralInputProperties.inputUnclamped;
			}
		}

		/// <summary>
		/// Method called by strafe input started
		/// </summary>
		protected virtual void OnStrafeStarted()
		{
			if (startStrafeMode != null)
			{
				startStrafeMode();
			}

			currentForwardInputProperties = configuration.strafeForwardMovementProperties;
			currentLateralInputProperties = configuration.strafeLateralMovementProperties;
			movementMode = ThirdPersonMotorMovementMode.Strafe;
			

			//thirdPersonBrain.cameraAnimationManager.SetAnimation("Strafe",1);
			thirdPersonBrain.thirdPersonCameraAnimationManager.StrafeStarted();

			turning = true;
			var cameraForward = Camera.main.transform.forward;
			cameraForward.y = 0; 
			//cameraForward.z = 0;
			gameObject.transform.forward = cameraForward;
			//SetStrafeLookDirectionTransition(cameraForward);
		//	SetStartStrafeLookDirection();

		}

		private bool turning;
		private Quaternion lookDirection;

		/// <summary>
		/// Method called by strafe input ended
		/// </summary>
		protected virtual void OnStrafeEnded()
		{
			if (startActionMode != null)
			{
				startActionMode();
			}
			
			Debug.Log("Strafe End");
			
		//	thirdPersonBrain.cameraAnimationManager.SetAnimation("Action",1);
			thirdPersonBrain.thirdPersonCameraAnimationManager.StrafeEnded();
		
			currentForwardInputProperties = configuration.forwardMovementProperties;
			currentLateralInputProperties = null;
			movementMode = ThirdPersonMotorMovementMode.Action;
			/*
			 * currentForwardInputProperties = configuration.forwardMovementProperties;
			currentLateralInputProperties = null;
			movementMode = ThirdPersonMotorMovementMode.Action;
			 */
			
			//TODO Adjust method for calling these animations that control the state driven camera

		}

		
		

		/// <summary>
		/// Called by update to handle movement
		/// </summary>
		protected virtual void HandleMovement()
		{
			if (movementState == ThirdPersonGroundMovementState.TurningAround)
			{
				CalculateForwardMovement();
				return;
			}

			switch (movementMode)
			{
				case ThirdPersonMotorMovementMode.Action:
					ActionMovement();
					break;
				case ThirdPersonMotorMovementMode.Strafe:
					StrafeMovement();
					break;
			}
		}

		protected virtual void ActionMovement()
		{
			SetLookDirection();
			CalculateForwardMovement();
		}

		protected virtual void StrafeMovement()
		{
			SetStrafeLookDirection();
			CalculateStrafeMovement();
		}

		protected virtual void SetStrafeLookDirection()
		{
			
			Vector3 lookForwardY = transform.rotation.eulerAngles;

			lookForwardY.x = 0;
			lookForwardY.z = 0;
			lookForwardY.y -= characterInput.lookInput.x * Time.deltaTime * configuration.scaleStrafeLook;
			
			Quaternion targetRotation = Quaternion.Euler(lookForwardY);

			targetYRotation = targetRotation.eulerAngles.y;

			Quaternion newRotation =
				Quaternion.RotateTowards(transform.rotation, targetRotation,
				                         configuration.turningYSpeed * Time.deltaTime);

			SetTurningSpeed(transform.rotation, newRotation);

			transform.rotation = newRotation;
		}
		
		protected virtual void SetLookDirection()
		{
			if (!characterInput.hasMovementInput)
			{
				normalizedTurningSpeed = 0;
				return;
			}

			Quaternion targetRotation = CalculateTargetRotation();
			targetYRotation = targetRotation.eulerAngles.y;

			if (characterPhysics.isGrounded && CheckForAndHandleRapidTurn(targetRotation))
			{
				return;
			}

			float turnSpeed = characterPhysics.isGrounded
				? configuration.turningYSpeed
				: configuration.jumpTurningYSpeed;

			Quaternion newRotation =
				Quaternion.RotateTowards(transform.rotation, targetRotation,
				                         turnSpeed * Time.deltaTime);

			SetTurningSpeed(transform.rotation, newRotation);

			transform.rotation = newRotation;
		}
		protected virtual void SetStartStrafeLookDirection()
		{
			var cameraForward = Camera.main.transform.forward;
			cameraForward.y = 0;
			
			//cameraForward.z = 0;
			// gameObject.transform.forward = cameraForward;
			
			Quaternion targetRotation = Quaternion.LookRotation(cameraForward);
			//Quaternion targetRotation = CalculateTargetRotation();
			targetYRotation = targetRotation.eulerAngles.y;

			float turnSpeed = characterPhysics.isGrounded
				? configuration.turningYSpeed
				: configuration.jumpTurningYSpeed;

			Quaternion newRotation =
				Quaternion.RotateTowards(transform.rotation, targetRotation,
				                         turnSpeed * Time.deltaTime);

			SetTurningSpeed(transform.rotation, newRotation);

			transform.rotation = newRotation;
		}
		

		protected virtual void CalculateForwardMovement()
		{
			normalizedLateralSpeed = 0;
			ApplyForwardInput(characterInput.moveInput.magnitude);
			
			Vector3 groundMovementVector = animator.deltaPosition * configuration.scaleRootMovement;
			groundMovementVector.y = 0;
			averageForwardMovement =
				(averageForwardMovement + groundMovementVector.GetMagnitudeOnAxis(transform.forward)) / 2f;
		}

		protected virtual void CalculateStrafeMovement()
		{
			normalizedForwardSpeed = (Mathf.Approximately(characterInput.moveInput.y, 0f) ? 0f : characterInput.moveInput.y)
				* configuration.strafeForwardMovementProperties.inputUnclamped;
			normalizedLateralSpeed = Mathf.Approximately (characterInput.moveInput.x, 0f) ? 0f : characterInput.moveInput.x
				* configuration.strafeLateralMovementProperties.inputUnclamped;
		}

		protected virtual void ApplyForwardInput(float input)
		{
			float forwardVelocity = currentForwardInputProperties.inputGain;
			if (Mathf.Abs(Mathf.Sign(input) - Mathf.Sign(normalizedForwardSpeed)) > 0)
			{
				forwardVelocity = currentForwardInputProperties.inputChangeGain;
			}

			var clamp = Mathf.Min(characterInput.moveInput.magnitude, forwardClampSpeed);
			normalizedForwardSpeed =
				Mathf.Clamp(normalizedForwardSpeed + input * forwardVelocity * Time.deltaTime, -clamp,
				            clamp);
		}

		protected virtual float DecelerateClampSpeed(float currentValue, float targetValue, float gain)
		{
			if (currentValue <= targetValue)
			{
				return targetValue;
			}

			return Mathf.Lerp(currentValue, targetValue, Time.deltaTime * gain);
		}

		protected virtual void HandleClampSpeedDeceleration()
		{
			forwardClampSpeed = DecelerateClampSpeed(forwardClampSpeed, targetForwardClampSpeed,
			                                         currentForwardInputProperties.inputDecay);

			if (isStrafing)
			{
				lateralClampSpeed = DecelerateClampSpeed(lateralClampSpeed, targetLateralClampSpeed,
				                                         currentLateralInputProperties.inputDecay);
			}
		}

		protected virtual Quaternion CalculateTargetRotation()
		{
			Vector3 flatForward = cameraTransform.forward;
			flatForward.y = 0f;
			flatForward.Normalize();

			Vector3 localMovementDirection =
				new Vector3(characterInput.moveInput.x, 0f, characterInput.moveInput.y);
			Quaternion cameraToInputOffset = Quaternion.FromToRotation(Vector3.forward, localMovementDirection);
			cameraToInputOffset.eulerAngles = new Vector3(0f, cameraToInputOffset.eulerAngles.y, 0f);

			return Quaternion.LookRotation(cameraToInputOffset * flatForward);
		}

		protected virtual void SetTurningSpeed(Quaternion currentRotation, Quaternion newRotation)
		{
			float currentY = currentRotation.eulerAngles.y;
			float newY = newRotation.eulerAngles.y;
			float difference = (MathUtilities.Wrap180(newY) - MathUtilities.Wrap180(currentY)) / Time.deltaTime;

			normalizedTurningSpeed = Mathf.Lerp(normalizedTurningSpeed,
			                                    Mathf.Clamp(
				                                    difference / configuration.turningYSpeed *
				                                    configuration.turningSpeedScaleVisual, -1, 1),
			                                    Time.deltaTime * configuration.turningLerpFactor);
		}

		protected virtual void TurnaroundComplete()
		{
			movementState = preTurnMovementState;
		}

		protected virtual bool CheckForAndHandleRapidTurn(Quaternion target)
		{
			if (turnaroundBehaviour == null)
			{
				return false;
			}

			float currentY = transform.eulerAngles.y;
			float newY = target.eulerAngles.y;
			float angle = MathUtilities.Wrap180(MathUtilities.Wrap180(newY) - MathUtilities.Wrap180(currentY));

			if (Mathf.Abs(angle) > configuration.angleRapidTurn)
			{
				preTurnMovementState = movementState;
				movementState = ThirdPersonGroundMovementState.TurningAround;
				turnaroundBehaviour.TurnAround(angle);
				return true;
			}

			return false;
		}
	}
}