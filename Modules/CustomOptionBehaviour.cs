using System;
using UnityEngine;

namespace EHR.Modules;

public class CustomOptionBehaviour(IntPtr cppPtr) : MonoBehaviour(cppPtr)
{
    public OptionBehaviour option;
    public OptionItem item;
    
    public void Start()
    {
        option = GetComponent<OptionBehaviour>();
    }
}