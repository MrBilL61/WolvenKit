﻿using System;
using System.Collections.Generic;
using System.Linq;
using WolvenKit.App.ViewModels.Shell;
using WolvenKit.RED4.Types;

namespace WolvenKit.App.Helpers;

/// <summary>
/// This class takes a <see cref="ChunkViewModel"/> and returns a list of compatible strings for the view. 
/// </summary>
public abstract class CvmDropdownHelper
{
    // Scanned through 1000+ .app files to find these. If you encounter any others, write a ticket :)
    private static readonly List<string> s_appFileRenderPlane =
        ["renderPlane", "renderPlaneLeftArm", "renderPlaneRightArm"];

    // Any tags for .app files
    private static readonly List<string> s_appTags =
    [
        "AppearanceParts", // enable partsValues for non-player equipment
        "hide_Head", "hide_Torso", "hide_LowerAbdomen", "hide_UpperAbdomen", "hide_CollarBone",
        "hide_Arms", "hide_Thighs", "hide_Calves", "hide_Ankles", "hide_Feet", "hide_Legs",
    ];

    // Any tags for root entity files
    private static readonly List<string> s_rootEntityTags =
    [
        "DynamicAppearance",
        "force_Hair",
        "HighHeels", "FlatShoes", "Boots", "Heels", "Sneakers", "Stilettos", "Metal_feet",
    ];

    // Any tags for mesh entity files
    private static readonly List<string> s_meshEntityTags =
    [
        "PlayerBodyPart", "Tight", "Normal", "Large", "XLarge"
    ];

    private static readonly List<string> s_questHandleParentNames =
    [
        "path", "contact", "caller", "addressee", "briefingPath", "mappinPath"
    ];
    
    public static Dictionary<string, string> GetDropdownOptions(ChunkViewModel cvm)
    {
        if (cvm.Parent is not ChunkViewModel parent)
        {
            return [];
        }

        IEnumerable<string?> ret = [];
        switch (parent.ResolvedData)
        {
            case gameJournalPath when cvm.Name is "className" && s_questHandleParentNames.Contains(parent.Name):
                ret = RedTypeHelper.GetExtendingClassNames(typeof(gameJournalEntry));
                break;
            case CArray<CName> when parent is { Name: "chunkMaterials", Parent.Parent.Parent.ResolvedData: CMesh mesh }:
                ret = mesh.MaterialEntries.Select(entry => entry.Name.GetResolvedText()).Distinct();
                break;
            case entISkinTargetComponent when cvm.Name is "renderingPlaneAnimationParam":
                ret = s_appFileRenderPlane;
                break;
            case CArray<CName> when parent is // tags in .app file appearance
            {
                Name: "tags", Parent: { ResolvedData: redTagList, Parent.ResolvedData: appearanceAppearanceDefinition }
            }:
                ret = s_appTags;
                break;
            // root entity tag
            case CArray<CName>
                when parent.Name is "tags" && cvm.GetRootModel() is
                    { ResolvedData: entEntityTemplate { Entity.Chunk: gameObject } }:
                ret = s_rootEntityTags;
                break;
            // mesh entity tag
            case CArray<CName>
                when parent.Name is "tags" && cvm.GetRootModel() is
                    { ResolvedData: entEntityTemplate { Entity.Chunk: not gameObject } }:
                ret = s_meshEntityTags;
                break;
            default:
                break;
        }

        return ret.Where(x => !string.IsNullOrEmpty(x)).ToDictionary(x => x!, y => y!);
    }

    /// <summary>
    /// Should this CVM show dropdown options at all?
    /// </summary>
    public static bool HasDropdownOptions(ChunkViewModel cvm)
    {
        if (cvm.Parent is not ChunkViewModel parent)
        {
            return false;
        }

        if (parent.ResolvedData is gameJournalPath)
        {
            return cvm.Name is "className" && s_questHandleParentNames.Contains(parent.Name);
        }
        
        return parent.ResolvedData switch
        {
            gameJournalPath when cvm.Name is "className" && parent.Name is "path" => true,
            CArray<CName> when parent is { Name: "chunkMaterials", Parent.Parent.Parent.ResolvedData: CMesh mesh } =>
                true,
            CArray<CName> when parent.Name is "tags" && cvm.GetRootModel() is
                { ResolvedData: entEntityTemplate ent } => true,
            _ => false
        };
    }
}