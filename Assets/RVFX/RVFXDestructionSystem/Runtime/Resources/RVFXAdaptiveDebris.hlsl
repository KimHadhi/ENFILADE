// One compact contact record per point. Original packed motion stays untouched.
// Preparation and evaluation are entirely GPU-side; no motion readback/repack.
struct DebrisEmitter { float4 source; float4 rotation; float4 timing; };
struct DebrisContact
{
    float4 start;      // mapped world launch, absolute spawn frame (-1 = suppressed)
    float4 turn;       // local rotation delta
    float4 cut;        // mapped world cutoff, local source cutoff frame
    float4 velocity;   // world exit velocity, original contact frame
    float4 impact;     // world landing, local landing time (-1 = no surface)
    float4 edge;       // ledge exit world position, local exit time
    float4 edgeSpeed;  // ledge exit velocity, output lifetime in frames
    float4 edgeRest;   // lower landing position, local landing time
};
StructuredBuffer<DebrisEmitter> _DebrisLaunches;
StructuredBuffer<DebrisEmitter> _DebrisLifetimeInputs;
int _DebrisTimelineFrames;
StructuredBuffer<int> _DebrisEmitterIndices;
RWStructuredBuffer<DebrisContact> _DebrisContacts;
Texture2D<float> _DebrisHeightMap;
int _DebrisUseMap;
float _DebrisFPS, _DebrisFloor, _DebrisReferenceFloor, _DebrisFlight, _DebrisWorldScale;
float4 _DebrisMapRect, _DebrisWorldRotation;
float3 _DebrisGravity;
float4x4 _DebrisWorldToLocal;

float4 DQM(float4 a,float4 b) { return float4(a.w*b.xyz+b.w*a.xyz+cross(a.xyz,b.xyz),a.w*b.w-dot(a.xyz,b.xyz)); }
float4 DQI(float4 q) { return float4(-q.xyz,q.w); }
float3 DQR(float4 q,float3 p) { float3 t=2*cross(q.xyz,p);return p+q.w*t+cross(q.xyz,t); }
float4 DWorldTurn(DebrisContact c) { return DQM(DQM(_DebrisWorldRotation,c.turn),DQI(_DebrisWorldRotation)); }
float DHeight(float3 p)
{
    if(_DebrisUseMap==0)return _DebrisFloor;
    float2 uv=(p.xz-_DebrisMapRect.xy)/_DebrisMapRect.zw;
    if(any(uv<0)||any(uv>1))return -1e20;
    uint w,h;_DebrisHeightMap.GetDimensions(w,h);
    int2 a=(int2)floor(uv*float2(w,h)-.5),lim=int2(w-1,h-1);
    float h0=_DebrisHeightMap.Load(int3(clamp(a,int2(0,0),lim),0));
    float h1=_DebrisHeightMap.Load(int3(clamp(a+int2(1,0),int2(0,0),lim),0));
    float h2=_DebrisHeightMap.Load(int3(clamp(a+int2(0,1),int2(0,0),lim),0));
    float h3=_DebrisHeightMap.Load(int3(clamp(a+1,int2(0,0),lim),0));
    return max(max(h0,h1),max(h2,h3));
}
void DSource(PointData pt,float f,out float3 p,out float4 q)
{
    f=clamp(f,0,pt.sampleCount-1);int a=(int)f,b=min(a+1,pt.sampleCount-1);
    float3 ap,bp;float4 aq,bq;
    DecodeSample(_SampleBuffer[pt.sampleOffset+a],ap,aq);
    DecodeSample(_SampleBuffer[pt.sampleOffset+b],bp,bq);
    p=mul(_LocalToWorld,float4(lerp(ap,bp,frac(f)),1)).xyz;
    q=NlerpQuaternion(aq,bq,frac(f));
}
float3 DFlight(PointData pt,DebrisContact c,float t)
{
    float f=t*_DebrisFPS;
    if(f>c.cut.w)
    {float tail=t-c.cut.w/_DebrisFPS;return c.cut.xyz+c.velocity.xyz*tail+_DebrisGravity*(.5*tail*tail);}
    float3 p,origin;float4 q;DSource(pt,f,p,q);DSource(pt,0,origin,q);
    return c.start.xyz+DQR(DWorldTurn(c),p-origin);
}
float3 DTail(PointData pt,DebrisContact c,float t,float radius,out float frame)
{
    frame=clamp(c.velocity.w+(t-c.impact.w)*_DebrisFPS,0,pt.sampleCount-1);
    float3 p,hit;float4 q;DSource(pt,frame,p,q);DSource(pt,c.velocity.w,hit,q);
    float3 d=DQR(DWorldTurn(c),p-hit);
    float3 result=c.impact.xyz+float3(d.x,0,d.z);
    result.y=DHeight(result)+radius+.002+max(0,p.y-_DebrisReferenceFloor-radius);
    return result;
}
[numthreads(64,1,1)]
void PrepareDebrisContacts(uint3 tid:SV_DispatchThreadID)
{
    int i=tid.x;if(i>=_PointCount)return;
    PointData pt=_PointBuffer[i];DebrisContact c=(DebrisContact)0;
    c.start.w=-1;c.impact.w=-1;c.edge.w=-1;c.edgeRest.w=-1;
    if(pt.sampleCount<=0){_DebrisContacts[i]=c;return;}
    DebrisEmitter emitter=_DebrisLaunches[_DebrisEmitterIndices[i]];
    c.start.w=emitter.source.w;c.turn=emitter.rotation;
    if(c.start.w<0){_DebrisContacts[i]=c;return;}
    float3 p,origin,previous;float4 q;
    DSource(pt,0,origin,q);previous=origin;
    float3 localOrigin=mul(_DebrisWorldToLocal,float4(origin,1)).xyz;
    c.start.xyz=mul(_LocalToWorld,float4(emitter.source.xyz+DQR(c.turn,localOrigin-emitter.timing.xyz),1)).xyz;
    float radius=max(.001,pt.scaleRadius.w*_DebrisWorldScale);
    if(emitter.timing.w>.5)
    {float h=DHeight(c.start.xyz);if(h>-1e18)c.start.y=h+radius;}
    int originalHit=-1;
    for(int f=1;f<pt.sampleCount;f++)
    {DSource(pt,f,p,q);if(p.y-radius<=_DebrisReferenceFloor+.025 && p.y<=previous.y+.001){originalHit=f;break;}previous=p;}
    c.cut.w=originalHit<0?pt.sampleCount-1:max(0,originalHit-2);
    DSource(pt,c.cut.w,p,q);DSource(pt,max(0,c.cut.w-1),previous,q);
    float3 velocity=(p-previous)*_DebrisFPS;
    if(c.cut.w==0){DSource(pt,1,previous,q);velocity=(previous-p)*_DebrisFPS;}
    c.cut.xyz=c.start.xyz+DQR(DWorldTurn(c),p-origin);
    c.velocity.xyz=DQR(DWorldTurn(c),velocity);
    c.velocity.w=originalHit<0?pt.sampleCount-1:originalHit;
    float dt=1/_DebrisFPS,limit=(pt.sampleCount-1)*dt+_DebrisFlight,step=max(dt*.25,limit/8192);
    float3 last=DFlight(pt,c,0);
    for(int s=0;s<=8192;s++)
    {
        float t=min(limit,s*step);p=DFlight(pt,c,t);float h=DHeight(p);
        bool descending=s>0&&p.y<=last.y;
        if(h>-1e18&&p.y-radius<=h&&(descending||p.y-radius<h-.01))
        {
            float lo=max(0,t-step),hi=t;
            for(int j=0;j<8;j++){float mid=(lo+hi)*.5;float3 mp=DFlight(pt,c,mid);if(mp.y-radius>DHeight(mp))lo=mid;else hi=mid;}
            c.impact=float4(DFlight(pt,c,hi),hi);c.impact.y=max(c.impact.y,DHeight(c.impact.xyz)+radius+.002);break;
        }
        if(t>=limit)break;last=p;
    }
    c.edgeSpeed.w=pt.sampleCount+(c.impact.w>=0?max(0,ceil(c.impact.w*_DebrisFPS-c.velocity.w)):ceil(_DebrisFlight*_DebrisFPS));
    // Preserve the explicit Keep Until Bake End policy across an extended timeline.
    // Timed debris keeps its recorded lifetime, even if it ends on the last frame.
    if(_DebrisLifetimeInputs[_DebrisEmitterIndices[i]].timing.z>.5)
        c.edgeSpeed.w=max(c.edgeSpeed.w,_DebrisTimelineFrames-c.start.w);
    // Resolve a ledge exit once so seeking never depends on previous playback frames.
    if(c.impact.w>=0&&_DebrisUseMap!=0)
    {
        float3 prev=DFlight(pt,c,max(0,c.impact.w-dt)),before=prev;
        for(int f=(int)ceil(c.impact.w*_DebrisFPS);f<(int)c.edgeSpeed.w;f++)
        {
            float t=f*dt,sourceFrame;
            p=DTail(pt,c,t,radius,sourceFrame);
            if(c.edge.w<0&&DHeight(p)<-1e18){c.edge=float4(prev,t-dt);c.edgeSpeed.xyz=(prev-before)/dt;}
            if(c.edge.w>=0)
            {
                float elapsed=t-c.edge.w;p=c.edge.xyz+c.edgeSpeed.xyz*elapsed+_DebrisGravity*(.5*elapsed*elapsed);
                float h=DHeight(p);
                if(h>-1e18&&p.y-radius<=h&&(c.edgeSpeed.y+_DebrisGravity.y*elapsed)<=0)
                {c.edgeRest=float4(p.x,h+radius+.002,p.z,t);break;}
            }
            before=prev;prev=p;
        }
    }
    _DebrisContacts[i]=c;
}
bool DAdaptivePose(int i,PointData pt,out float3 localPosition,out float4 rotation)
{
    DebrisContact c=_DebrisContacts[i];float frame=_Frame-c.start.w;
    localPosition=0;rotation=float4(0,0,0,1);
    if(c.start.w<0||frame<0||frame>c.edgeSpeed.w-1)return false;
    float t=frame/_DebrisFPS,radius=max(.001,pt.scaleRadius.w*_DebrisWorldScale);
    float3 p;
    if(c.impact.w<0||t<c.impact.w)p=DFlight(pt,c,t);
    else p=DTail(pt,c,t,radius,frame);
    if(c.edge.w>=0&&t>=c.edge.w)
    {float e=t-c.edge.w;p=c.edge.xyz+c.edgeSpeed.xyz*e+_DebrisGravity*(.5*e*e);}
    if(c.edgeRest.w>=0&&t>=c.edgeRest.w)p=c.edgeRest.xyz;
    float3 ignored;DSource(pt,frame,ignored,rotation);rotation=NormalizeQuaternion(DQM(c.turn,rotation));
    localPosition=mul(_DebrisWorldToLocal,float4(p,1)).xyz;return true;
}
