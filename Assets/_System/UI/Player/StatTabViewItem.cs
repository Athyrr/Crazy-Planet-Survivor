using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class StatTabViewItem : UIViewItemBase
{
    public TMP_Text Label;
    public TMP_Text Value;



    public  void Refresh(string labelText, string valueText)
    {
        Label.text = labelText;
        Value.text = valueText;
    }

    public override void OnPointerEnter(PointerEventData eventData)
    {
        throw new System.NotImplementedException();
    }
}