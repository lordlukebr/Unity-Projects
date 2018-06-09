﻿using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;

public class FireProjectileV2 : MonoBehaviour 
{
	public RaycastHit targetHit;	
	public GameObject[] projectiles;
	public Transform spawnPosition;
	public GameObject targetTexture;
	GameObject targetTextureInst;
	StaminaSystem stamina;
	UIButtonData fireButton;

	public int currentProjectile = 0;
	public float speed = 1000;	

	public struct LaunchData
	{
		public Vector3 toTarget, velocity;
		public float gSquared, b, discriminant;
	}
	public LaunchData launchData;

	void Start()
	{
		targetTextureInst = Instantiate(targetTexture);
		stamina = GameObject.Find("Stamina Bar").GetComponent<StaminaSystem>();
		fireButton = GameObject.Find("Fire Button").GetComponent<UIButtonData>();
	}

	void Update () 
	{
		Ray ray = Camera.main.ScreenPointToRay(new Vector2(Screen.width/2, Screen.height/2));

		Debug.DrawRay(Camera.main.ScreenPointToRay(new Vector2(Screen.width / 2, Screen.height / 2)).origin, 
						Camera.main.ScreenPointToRay(new Vector2(Screen.width / 2, Screen.height / 2)).direction * 100, Color.yellow);

		if (Physics.Raycast(ray, out targetHit, 1000f, 1<<9))
		{
			targetTextureInst.transform.position = targetHit.point;
			targetTextureInst.transform.rotation = Quaternion.FromToRotation(targetTextureInst.transform.up, targetHit.normal) * targetTextureInst.transform.rotation;

			CalculateLaunchData();

			if (fireButton._click)
			{				
				if (launchData.discriminant > 0 && stamina.canFire(1f))
				{						
					Launch();
					stamina.decreaseValue(1f);
				}				
			}
		}
	}	
	
	void Launch()
	{
		GameObject projectile = Instantiate(projectiles[currentProjectile], spawnPosition.position, Quaternion.identity) as GameObject;
		projectile.transform.LookAt(launchData.velocity);
		// Apply the calculated velocity (do not use force, acceleration, or impulse modes)
		projectile.GetComponent<Rigidbody>().AddForce(launchData.velocity, ForceMode.VelocityChange);			
	}

	void CalculateLaunchData()
	{
		launchData.toTarget = targetHit.point - transform.position;

		// Set up the terms we need to solve the quadratic equations.
		launchData.gSquared = Physics.gravity.sqrMagnitude;
		launchData.b = speed * speed + Vector3.Dot(launchData.toTarget, Physics.gravity);
		launchData.discriminant = launchData.b * launchData.b - launchData.gSquared * launchData.toTarget.sqrMagnitude;

		if (launchData.discriminant >= 0)
		{
			float discRoot = Mathf.Sqrt(launchData.discriminant);

			// Highest shot with the given max speed:
			//float time_max = Mathf.Sqrt((launchData.b + discRoot) * 2f / launchData.gSquared);

			// Most direct shot with the given max speed:
			float time_min = Mathf.Sqrt((launchData.b - discRoot) * 2f / launchData.gSquared);

			// Lowest-speed arc available:
			//float time_lowEnergy = Mathf.Sqrt(Mathf.Sqrt(launchData.toTarget.sqrMagnitude * 4f / launchData.gSquared));

			float time = time_min; // choose T_max, T_min, or some T in-between like T_lowEnergy

			// Convert from time-to-target to a launch velocity:
			launchData.velocity = launchData.toTarget / time - Physics.gravity * time / 2f;

			targetTextureInst.GetComponentInChildren<Light>().color = Color.cyan;
		}
		else targetTextureInst.GetComponentInChildren<Light>().color = Color.red;
	}
}