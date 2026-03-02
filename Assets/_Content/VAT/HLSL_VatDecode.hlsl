#ifndef HLSL_VATDECODE_INCLUDED
#define HLSL_VATDECODE_INCLUDED

/* Renvoie la coordonnée pixel (x,y) dans la texture VAT */
void GetVATPixel_float(uint vertexId, float frameIdx, uint2 texWidthHeight, float2 texelWidth, uint frames, out float2 uvVertex, out float2 uvNormal)
{
    float  col       =  vertexId % texWidthHeight.x;// colonne
    float  rowBlock  =  vertexId / texWidthHeight.x;// bloc de sommets
    float rowPos = rowBlock * (frames+1) + frameIdx;
    
    float2 pix = float2(col, rowPos);//pixel dans texture

    //on se met pile poil ou il faut pour la normal (moitié verticale)
    float heightPos = texWidthHeight.y * 0.5f;
    float rowNrm = rowPos + heightPos;
    float2 pix2 = float2(col, rowNrm);//pixel dans texture

    // 0.5 = centre du texel
    uvVertex = (float2(pix) + 0.5) * texelWidth; 
    uvNormal = (float2(pix2) + 0.5) * texelWidth;
}

#endif // HLSL_VATDECODE_INCLUDED
