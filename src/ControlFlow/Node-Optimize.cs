using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace Decompiler.ControlFlow
{
	public abstract partial class Node
	{
		public void Optimize()
		{
			if (Options.ReduceLoops) {
				OptimizeLoops();
			}
			if (Options.ReduceConditonals) {
				OptimizeIf();
			}
		}
		
		public void OptimizeLoops()
		{
		Reset:
			foreach(Node child in this.Childs) {
				if (child.Predecessors.Count == 1) {
					if (Options.ReduceGraph <= 0) return;
					Node predecessor = child.Predecessors[0];
					Node mergedNode;
					if (child.Successors.Contains(predecessor)) {
						mergedNode = MergeChilds<Loop>(predecessor, child);
					} else {
						mergedNode = MergeChilds<AcyclicGraph>(predecessor, child);
					}
					mergedNode.FalttenAcyclicChilds();
					goto Reset;
				}
			}
			// If the result is single acyclic node, eliminate it
			if (this.Childs.Count == 1 && this.HeadChild is AcyclicGraph) {
				if (Options.ReduceGraph-- <= 0) return;
				Node headChild = this.HeadChild;
				this.Childs.Remove(this.HeadChild);
				headChild.Childs.MoveTo(this);
			}
		}
		
		NodeCollection GetReachableNodes(Node exclude)
		{
			NodeCollection reachableNodes = new NodeCollection();
			reachableNodes.Add(this);
			for(int i = 0; i < reachableNodes.Count; i++) {
				reachableNodes.AddRange(reachableNodes[i].Successors);
				reachableNodes.Remove(exclude);
			}
			return reachableNodes;
		}
		
		public void OptimizeIf()
		{
			foreach(Node child in this.Childs) {
				if (child is Loop) {
					child.OptimizeIf();
				}
			}
			
			Node conditionNode = this.HeadChild;
			// Find conditionNode (the start)
			while(conditionNode != null) {
				if (conditionNode is BasicBlock && conditionNode.Successors.Count == 2) {
					// Found if start
					OptimizeIf((BasicBlock)conditionNode);
					conditionNode = this.HeadChild;
					continue; // Restart
				} else if (conditionNode.Successors.Count > 0) {
					// Keep looking down
					conditionNode = conditionNode.Successors[0];
					if (conditionNode == this.HeadChild) {
						return;
					}
					continue; // Next
				} else {
					return; // End of block
				}
			}
		}
		
		public static void OptimizeIf(BasicBlock condition)
		{
			Node trueStart = condition.FloatUpToNeighbours(condition.FallThroughBasicBlock);
			Node falseStart = condition.FloatUpToNeighbours(condition.BranchBasicBlock);
			Debug.Assert(trueStart != null);
			Debug.Assert(falseStart != null);
			Debug.Assert(trueStart != falseStart);
			
			NodeCollection trueReachable = trueStart.GetReachableNodes(condition);
			NodeCollection falseReachable = falseStart.GetReachableNodes(condition);
			NodeCollection commonReachable = NodeCollection.Intersect(trueReachable, falseReachable);
			
			NodeCollection trueNodes = trueReachable.Clone();
			trueNodes.RemoveRange(commonReachable);
			NodeCollection falseNodes = falseReachable.Clone();
			falseNodes.RemoveRange(commonReachable);
			
			// Replace the basic block with condition node
			if (Options.ReduceGraph-- <= 0) return;
			Node conditionParent = condition.Parent;
			int conditionIndex = condition.Index; 
			ConditionalNode conditionalNode = new ConditionalNode(condition);
			conditionalNode.MoveTo(conditionParent, conditionIndex);
			
			if (Options.ReduceGraph-- <= 0) return;
			trueNodes.MoveTo(conditionalNode.TrueBody);
			
			if (Options.ReduceGraph-- <= 0) return;
			falseNodes.MoveTo(conditionalNode.FalseBody);
			
			// Optimize the created subtrees
			conditionalNode.TrueBody.OptimizeIf();
			conditionalNode.FalseBody.OptimizeIf();
		}
	}
}
