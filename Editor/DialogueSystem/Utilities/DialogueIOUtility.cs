using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace Insignia.DialogueSystem.Utilities
{
    using Windows;
    using Elements;
    using Data.Save;
    using Data;
    using Enumerations;
    using Events;
    using ScriptableObjects;
    
    public class DialogueIOUtility
    {
        private static DialogueGraphView graphView;
        
        private static string graphFileName;
        private static string containerFolderPath;

        private static List<DialogueGroup> groups;
        private static List<DialogueNode> nodes;

        private static Dictionary<string, DialogueGroupSO> createdDialogueGroups;
        private static Dictionary<string, DialogueSO> createdDialogues;

        private static Dictionary<string, DialogueGroup> loadedGroups;
        private static Dictionary<string, DialogueNode> loadedNodes;

        public static void Initialize(DialogueGraphView _graphView, string graphName)
        {
            graphView = _graphView;
            
            graphFileName = graphName;
            containerFolderPath = $"Assets/DialogueSystem/Dialogues/{graphFileName}";

            groups = new List<DialogueGroup>();
            nodes = new List<DialogueNode>();

            createdDialogueGroups = new Dictionary<string, DialogueGroupSO>();
            createdDialogues = new Dictionary<string, DialogueSO>();

            loadedGroups = new Dictionary<string, DialogueGroup>();
            loadedNodes = new Dictionary<string, DialogueNode>();
        }
        
        #region Save Methods

        public static void Save()
        {
            CreateStaticFolders();

            GetElementsFromGraph();

            DialogueGraphSaveDataSO graphData = CreateAsset<DialogueGraphSaveDataSO>("Assets/Editor/DialogueSystem/Graphs", $"{graphFileName}Graph");
            graphData.Initialize(graphFileName);

            DialogueContainerSO dialogueContainer = CreateAsset<DialogueContainerSO>(containerFolderPath, graphFileName);
            dialogueContainer.Initialize(graphFileName);

            SaveGroups(graphData, dialogueContainer);
            SaveNodes(graphData, dialogueContainer);
            
            SaveAsset(graphData);
            SaveAsset(dialogueContainer);
        }

        #region Groups

        private static void SaveGroups(DialogueGraphSaveDataSO graphData, DialogueContainerSO dialogueContainer)
        {
            List<string> groupNames = new List<string>();

            foreach (DialogueGroup group in groups)
            {
                SaveGroupToGraph(group, graphData);
                SaveGroupToScriptableObject(group, dialogueContainer);
                
                groupNames.Add(group.title);
            }

            UpdateOldGroups(groupNames, graphData);
        }

        private static void SaveGroupToGraph(DialogueGroup group, DialogueGraphSaveDataSO graphData)
        {
            DialogueGroupSaveData groupData = new DialogueGroupSaveData()
            {
                ID = group.ID,
                Name = group.title,
                Position = group.GetPosition().position
            };
            
            graphData.Groups.Add(groupData);
        }
        
        private static void SaveGroupToScriptableObject(DialogueGroup group, DialogueContainerSO dialogueContainer)
        {
            string groupName = group.title;
            
            CreateFolder($"{containerFolderPath}/Groups", groupName);
            CreateFolder($"{containerFolderPath}/Groups/{groupName}", "Dialogues");

            DialogueGroupSO dialogueGroup = CreateAsset<DialogueGroupSO>($"{containerFolderPath}/Groups/{groupName}", groupName);
            dialogueGroup.Initialize(groupName);
            
            createdDialogueGroups.Add(group.ID, dialogueGroup);
            
            dialogueContainer.DialogueGroups.Add(dialogueGroup, new List<DialogueSO>());

            SaveAsset(dialogueGroup);
        }
        
        private static void UpdateOldGroups(List<string> currentGroupNames, DialogueGraphSaveDataSO graphData)
        {
            if (graphData.OldGroupNames != null && graphData.OldGroupNames.Count != 0)
            {
                List<string> groupsToRemove = graphData.OldGroupNames.Except(currentGroupNames).ToList();

                foreach (string groupToRemove in groupsToRemove)
                {
                    RemoveFolder($"{containerFolderPath}/Groups/{groupsToRemove}");
                }
            }

            graphData.OldGroupNames = new List<string>(currentGroupNames);
        }

        #endregion

        #region Nodes

        private static void SaveNodes(DialogueGraphSaveDataSO graphData, DialogueContainerSO dialogueContainer)
        {
            SerializableDictionary<string, List<string>> groupedNodeNames = new SerializableDictionary<string, List<string>>();
            List<string> ungroupedNodeNames = new List<string>();
            
            foreach (DialogueNode node in nodes)
            {
                SaveNodeToGraph(node, graphData);
                SaveNodeToScriptableObject(node, dialogueContainer);

                if (node.Group != null)
                {
                    groupedNodeNames.AddItem(node.Group.title, node.DialogueName);
                    
                    continue;
                }
                
                ungroupedNodeNames.Add(node.DialogueName);
            }

            UpdateDialogueChoicesConnections();

            UpdateOldGroupedNodes(groupedNodeNames, graphData);
            UpdateOldUngroupedNodes(ungroupedNodeNames, graphData);
        }

        private static void SaveNodeToGraph(DialogueNode node, DialogueGraphSaveDataSO graphData)
        {
            List<DialogueChoiceSaveData> choices = CloneNodeChoices(node.Choices);

            DialogueNodeSaveData nodeData = new DialogueNodeSaveData()
            {
                ID = node.ID,
                Name = node.DialogueName,
                Choices = choices,
                Text = node.Text,
                DialogueEvent = node.DialogueEvent,
                DialogueAudio = node.DialogueAudio,

                GroupID = node.Group?.ID,
                DialogueType = node.DialogueType,
                Position = node.GetPosition().position
            };

            graphData.Nodes.Add(nodeData);
        }

        private static void SaveNodeToScriptableObject(DialogueNode node, DialogueContainerSO dialogueContainer)
        {
            DialogueSO dialogue;

            if (node.Group != null)
            {
                dialogue = CreateAsset<DialogueSO>($"{containerFolderPath}/Groups/{node.Group.title}/Dialogues",
                    node.DialogueName);
                
                dialogueContainer.DialogueGroups.AddItem(createdDialogueGroups[node.Group.ID], dialogue);
            }
            else
            {
                dialogue = CreateAsset<DialogueSO>($"{containerFolderPath}/Global/Dialogues", node.DialogueName);
                
                dialogueContainer.UngroupedDialogues.Add(dialogue);
            }
            
            dialogue.Initalize(
                node.DialogueName,
                node.Text,
                node.DialogueType,
                node.IsStartingNode(),
                ConvertNodeChoicesToDialogueChoices(node.Choices),
                node.DialogueEvent,
                node.DialogueAudio
            );

            createdDialogues.Add(node.ID, dialogue);
            
            SaveAsset(dialogue);
        }

        private static List<DialogueChoiceData> ConvertNodeChoicesToDialogueChoices(List<DialogueChoiceSaveData> nodeChoices)
        {
            List<DialogueChoiceData> dialogueChoices = new List<DialogueChoiceData>();

            foreach (DialogueChoiceSaveData nodeChoice in nodeChoices)
            {
                DialogueChoiceData choiceData = new DialogueChoiceData()
                {
                    Text = nodeChoice.Text
                };
                
                dialogueChoices.Add(choiceData);
            }

            return dialogueChoices;
        }
        
        private static void UpdateDialogueChoicesConnections()
        {
            foreach (DialogueNode node in nodes)
            {
                DialogueSO dialogue = createdDialogues[node.ID];

                for (int choiceIndex = 0; choiceIndex < node.Choices.Count; ++choiceIndex)
                {
                    DialogueChoiceSaveData nodeChoice = node.Choices[choiceIndex];

                    if (string.IsNullOrEmpty(nodeChoice.NodeID))
                    {
                        continue;
                    }

                    dialogue.Choices[choiceIndex].NextDialogue = createdDialogues[nodeChoice.NodeID];
                    
                    SaveAsset(dialogue);
                }
            }
        }

        private static void UpdateOldGroupedNodes(SerializableDictionary<string, List<string>> currentGroupedNodeNames,
            DialogueGraphSaveDataSO graphData)
        {
            if (graphData.OldGroupedNodeNames != null && graphData.OldGroupedNodeNames.Count != 0)
            {
                foreach (KeyValuePair<string, List<string>> oldGroupedNodes in graphData.OldGroupedNodeNames)
                {
                    List<string> nodesToRemove = new List<string>();

                    if (currentGroupedNodeNames.ContainsKey(oldGroupedNodes.Key))
                    {
                        nodesToRemove = oldGroupedNodes.Value.Except(currentGroupedNodeNames[oldGroupedNodes.Key])
                            .ToList();
                    }

                    foreach (string nodeToRemove in nodesToRemove)
                    {
                        RemoveAsset($"{containerFolderPath}/Groups/{oldGroupedNodes.Key}/Dialogues", nodeToRemove);
                    }
                }
            }

            graphData.OldGroupedNodeNames = new SerializableDictionary<string, List<string>>(currentGroupedNodeNames);
        }
        
        private static void UpdateOldUngroupedNodes(List<string> currentUngroupedNodeNames, DialogueGraphSaveDataSO graphData)
        {
            if (graphData.OldUngroupedNodeNames != null && graphData.OldUngroupedNodeNames.Count != 0)
            {
                List<string> nodesToRemove = graphData.OldUngroupedNodeNames.Except(currentUngroupedNodeNames).ToList();

                foreach (string nodeToRemove in nodesToRemove)
                {
                    RemoveAsset($"{containerFolderPath}/Global/Dialogues", nodeToRemove);
                }
            }

            graphData.OldUngroupedNodeNames = new List<string>(currentUngroupedNodeNames);
        }

        #endregion

        #endregion

        #region Load Methods

        public static void Load()
        {
            DialogueGraphSaveDataSO graphData =
                LoadAsset<DialogueGraphSaveDataSO>("Assets/Editor/DialogueSystem/Graphs", graphFileName);

            if (graphData == null)
            {
                EditorUtility.DisplayDialog(
                    "Couldn't load file.",
                    "The file at the following path could not be found:\n\n" +
                    $"Assets/Editor/DialogueSystem/Graphs/{graphFileName}\n\n" +
                    "Make sure you choose the right file and it's placed at the folder path mentioned above.",
                    "OK"
                );

                return;
            }
            
            DialogueEditorWindow.UpdateFileName(graphData.FileName);

            LoadGroups(graphData.Groups);
            LoadNodes(graphData.Nodes);
            LoadNodesConnectons();
        }

        private static void LoadGroups(List<DialogueGroupSaveData> groups)
        {
            foreach (DialogueGroupSaveData groupData in groups)
            {
                DialogueGroup group = graphView.CreateGroup(groupData.Name, groupData.Position);

                group.ID = groupData.ID;
                
                loadedGroups.Add(group.ID, group);
            }
        }

        private static void LoadNodes(List<DialogueNodeSaveData> nodes)
        {
            foreach (DialogueNodeSaveData nodeData in nodes)
            {
                List<DialogueChoiceSaveData> choices = CloneNodeChoices(nodeData.Choices);

                DialogueNode node = null;

                bool isObjectType = false;
                Type objectType = null;
                
                switch (nodeData.DialogueType)
                {
                    case DialogueType.SingleChoice:
                    case DialogueType.MultipleChoice:
                        node = graphView.CreateNode(nodeData.Name, nodeData.DialogueType, nodeData.Position, false);
                        isObjectType = false;

                        break;
                    case DialogueType.Event:
                        node = graphView.CreateNode(nodeData.Name, nodeData.DialogueType, nodeData.Position, false, nodeData.DialogueEvent.GetType());
                        node.DialogueEvent = nodeData.DialogueEvent;
                        
                        isObjectType = true;
                        objectType = nodeData.DialogueEvent.GetType();
                        
                        break;
                    case DialogueType.Audio:
                        node = graphView.CreateNode(nodeData.Name, nodeData.DialogueType, nodeData.Position, false, nodeData.DialogueAudio.GetType());
                        node.DialogueAudio = nodeData.DialogueAudio;
                        
                        isObjectType = true;
                        objectType = nodeData.DialogueAudio.GetType();
                        
                        break;
                }
                
                node.ID = nodeData.ID;
                node.Choices = choices;
                node.Text = nodeData.Text;

                node.Draw(isObjectType, objectType);
                
                graphView.AddElement(node);
                
                loadedNodes.Add(node.ID, node);

                if (string.IsNullOrEmpty(nodeData.GroupID))
                {
                    continue;
                }

                DialogueGroup group = loadedGroups[nodeData.GroupID];

                node.Group = group;
                
                group.AddElement(node);
            }
        }
        
        private static void LoadNodesConnectons()
        {
            foreach (KeyValuePair<string,DialogueNode> loadedNode in loadedNodes)
            {
                foreach (Port choicePort in loadedNode.Value.outputContainer.Children())
                {
                    DialogueChoiceSaveData choiceData = (DialogueChoiceSaveData)choicePort.userData;

                    if (string.IsNullOrEmpty(choiceData.NodeID))
                    {
                        continue;
                    }

                    DialogueNode nextNode = loadedNodes[choiceData.NodeID];

                    Port nextNodeInputPort = (Port)nextNode.inputContainer.Children().First();

                    Edge edge = choicePort.ConnectTo(nextNodeInputPort);
                    
                    graphView.AddElement(edge);

                    loadedNode.Value.RefreshPorts();
                }
            }
        }

        #endregion
        
        #region Creation Methods

        private static void CreateStaticFolders()
        {
            CreateFolder("Assets/Editor/DialogueSystem", "Graphs");
            
            CreateFolder("Assets", "DialogueSystem");
            CreateFolder("Assets/DialogueSystem", "Dialogues");
            CreateFolder("Assets/DialogueSystem/Dialogues", graphFileName);
            CreateFolder(containerFolderPath, "Global");
            CreateFolder(containerFolderPath, "Groups");
            CreateFolder($"{containerFolderPath}/Global", "Dialogues");
        }

        #endregion

        #region Fetch Methods

        private static void GetElementsFromGraph()
        {
            Type groupType = typeof(DialogueGroup);
            
            graphView.graphElements.ForEach(graphElement =>
            {
                if (graphElement is DialogueNode node)
                {
                    nodes.Add(node);
                    
                    return;
                }

                if (graphElement.GetType() == groupType)
                {
                    DialogueGroup group = (DialogueGroup)graphElement;
                    
                    groups.Add(group);
                    
                    return;
                }
            });
        }

        #endregion

        #region Utility Methods

        public static void CreateFolder(string path, string folderName)
        {
            if (AssetDatabase.IsValidFolder($"{path}/{folderName}"))
            {
                return;
            }

            AssetDatabase.CreateFolder(path, folderName);
        }
        
        public static void RemoveFolder(string fullPath)
        {
            FileUtil.DeleteFileOrDirectory($"{fullPath}.meta");
            FileUtil.DeleteFileOrDirectory($"{fullPath}/");
        }
        
        public static T CreateAsset<T>(string path, string assetName) where T : ScriptableObject
        {
            string fullPath = $"{path}/{assetName}.asset";

            T asset = LoadAsset<T>(path, assetName);

            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
            
                AssetDatabase.CreateAsset(asset, fullPath);
            }

            return asset;
        }

        public static T LoadAsset<T>(string path, string assetName) where T : ScriptableObject
        {
            string fullPath = $"{path}/{assetName}.asset";
            
            return AssetDatabase.LoadAssetAtPath<T>(fullPath);
        }

        public static void RemoveAsset(string path, string assetName)
        {
            AssetDatabase.DeleteAsset($"{path}/{assetName}.asset");
        }
        
        public static void SaveAsset(UnityEngine.Object asset)
        {
            EditorUtility.SetDirty(asset);
            
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        
        private static List<DialogueChoiceSaveData> CloneNodeChoices(List<DialogueChoiceSaveData> nodeChoices)
        {
            List<DialogueChoiceSaveData> choices = new List<DialogueChoiceSaveData>();

            foreach (DialogueChoiceSaveData choice in nodeChoices)
            {
                DialogueChoiceSaveData choiceData = new DialogueChoiceSaveData()
                {
                    Text = choice.Text,
                    NodeID = choice.NodeID
                };

                choices.Add(choiceData);
            }

            return choices;
        }

        #endregion
    }
}
