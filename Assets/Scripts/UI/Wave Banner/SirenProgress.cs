using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Graphic))]
public class SirenBannerController : MonoBehaviour
{
    [Header("발광 속도 및 주기")]
    [SerializeField] private float speed = 1.5f;          // 파동 진행 속도
    [SerializeField] private float cycleDelay = 0.5f;     // 한 번 퍼진 뒤 다음 발광까지 대기 시간

    private Material bannerMat;
    private static readonly int SirenProgressID = Shader.PropertyToID("_SirenProgress");
    private float timer = 0f;

    void Awake()
    {
        // UI 인스턴스 전용 매터리얼 복제 적용
        Graphic graphic = GetComponent<Graphic>();
        bannerMat = Instantiate(graphic.material);
        graphic.material = bannerMat;
    }

    void Update()
    {
        timer += Time.deltaTime * speed;
        float cycleTotal = 1.0f + cycleDelay;
        float current = timer % cycleTotal;

        // 0부터 1까지 중앙에서 퍼져나가고, 1 이후에는 대기 구간
        float progress = Mathf.Clamp01(current);
        bannerMat.SetFloat(SirenProgressID, progress);
    }

    void OnDestroy()
    {
        if (bannerMat != null)
            Destroy(bannerMat);
    }
}