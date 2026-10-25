using UnityEngine;

public interface IBreakable
{
    /// <summary>
    /// 물체가 파괴될 때 호출되는 함수
    /// </summary>
    /// <param name="hitPoint">충돌 지점</param>
    /// <param name="hitNormal">충돌 면의 법선</param>
    /// <param name="impactSpeed">충돌 당시 플레이어의 속도</param>
    void Break(Vector3 hitPoint, Vector3 hitNormal, float impactSpeed);
}