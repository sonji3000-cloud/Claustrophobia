using System;
using UnityEngine;

// Low-time ballistic solution for the same gravity-first steps used by TankBallisticStep.
public static class TankAimSolver
{
	public static bool TrySolve(Vector3 offset, Vector3 gravity, float speed, float step,
		float lifetime, out Vector3 direction, out float flightTime)
	{
		direction = Vector3.zero;
		flightTime = 0f;
		if (!IsFinite(offset) || !IsFinite(gravity) || !IsFinite(speed) || speed <= 0f
			|| !IsFinite(step) || step < 0f || !IsFinite(lifetime) || lifetime <= 0f) return false;

		double distanceSq = (double)offset.x * offset.x + (double)offset.y * offset.y + (double)offset.z * offset.z;
		if (distanceSq < 0.000001) return false;

		double gravitySq = (double)gravity.x * gravity.x + (double)gravity.y * gravity.y + (double)gravity.z * gravity.z;
		double dot = (double)offset.x * gravity.x + (double)offset.y * gravity.y + (double)offset.z * gravity.z;
		double speedSq = (double)speed * speed;
		double low = 0.0;
		double high = lifetime;

		if (gravitySq > 0.00000001)
		{
			// The required launch speed has one minimum for positive flight times.
			for (int i = 0; i < 40; i++)
			{
				double t = (low + high) * 0.5;
				double derivative = -2.0 * distanceSq + dot * step * t
					+ 0.5 * gravitySq * t * t * t * (t + step);
				if (derivative < 0.0) low = t;
				else high = t;
			}
			high = (low + high) * 0.5;
		}

		if (SpeedSq(offset, gravity, step, high) > speedSq * 1.00000001) return false;
		low = 0.0;
		for (int i = 0; i < 40; i++)
		{
			double t = (low + high) * 0.5;
			if (SpeedSq(offset, gravity, step, t) > speedSq) low = t;
			else high = t;
		}

		double time = (low + high) * 0.5;
		double x = offset.x / time - 0.5 * gravity.x * (time + step);
		double y = offset.y / time - 0.5 * gravity.y * (time + step);
		double z = offset.z / time - 0.5 * gravity.z * (time + step);
		double length = Math.Sqrt(x * x + y * y + z * z);
		direction = new Vector3((float)(x / length), (float)(y / length), (float)(z / length));
		flightTime = (float)time;
		return IsFinite(direction) && IsFinite(flightTime) && flightTime > 0f;
	}

	private static double SpeedSq(Vector3 offset, Vector3 gravity, double step, double time)
	{
		double x = offset.x / time - 0.5 * gravity.x * (time + step);
		double y = offset.y / time - 0.5 * gravity.y * (time + step);
		double z = offset.z / time - 0.5 * gravity.z * (time + step);
		return x * x + y * y + z * z;
	}

	private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
	private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
