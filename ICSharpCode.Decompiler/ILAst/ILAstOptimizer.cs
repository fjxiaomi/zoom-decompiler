using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ICSharpCode.Decompiler.FlowAnalysis;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.CSharp;

namespace ICSharpCode.Decompiler.ILAst
{
	public enum ILAstOptimizationStep
	{
		RemoveRedundantCode,
		ReduceBranchInstructionSet,
		InlineVariables,
		CopyPropagation,
		YieldReturn,
		SplitToMovableBlocks,
		TypeInference,
		SimplifyShortCircuit,
		SimplifyTernaryOperator,
		SimplifyNullCoalescing,
		TransformDecimalCtorToConstant,
		SimplifyLdObjAndStObj,
		TransformArrayInitializers,
		TransformCollectionInitializers,
		MakeAssignmentExpression,
		InlineVariables2,
		FindLoops,
		FindConditions,
		FlattenNestedMovableBlocks,
		RemoveRedundantCode2,
		GotoRemoval,
		DuplicateReturns,
		ReduceIfNesting,
		InlineVariables3,
		CachedDelegateInitialization,
		IntroduceFixedStatements,
		TypeInference2,
		RemoveRedundantCode3,
		None
	}
	
	public partial class ILAstOptimizer
	{
		int nextLabelIndex = 0;
		
		DecompilerContext context;
		TypeSystem typeSystem;
		ILBlock method;
		
		public void Optimize(DecompilerContext context, ILBlock method, ILAstOptimizationStep abortBeforeStep = ILAstOptimizationStep.None)
		{
			this.context = context;
			this.typeSystem = context.CurrentMethod.Module.TypeSystem;
			this.method = method;
			
			if (abortBeforeStep == ILAstOptimizationStep.RemoveRedundantCode) return;
			RemoveRedundantCode(method);
			
			if (abortBeforeStep == ILAstOptimizationStep.ReduceBranchInstructionSet) return;
			foreach(ILBlock block in method.GetSelfAndChildrenRecursive<ILBlock>()) {
				ReduceBranchInstructionSet(block);
			}
			// ReduceBranchInstructionSet runs before inlining because the non-aggressive inlining heuristic
			// looks at which type of instruction consumes the inlined variable.
			
			if (abortBeforeStep == ILAstOptimizationStep.InlineVariables) return;
			// Works better after simple goto removal because of the following debug pattern: stloc X; br Next; Next:; ldloc X
			ILInlining inlining1 = new ILInlining(method);
			inlining1.InlineAllVariables();
			
			if (abortBeforeStep == ILAstOptimizationStep.CopyPropagation) return;
			inlining1.CopyPropagation();
			
			if (abortBeforeStep == ILAstOptimizationStep.YieldReturn) return;
			YieldReturnDecompiler.Run(context, method);
			
			if (abortBeforeStep == ILAstOptimizationStep.SplitToMovableBlocks) return;
			foreach(ILBlock block in method.GetSelfAndChildrenRecursive<ILBlock>()) {
				SplitToBasicBlocks(block);
			}
			
			if (abortBeforeStep == ILAstOptimizationStep.TypeInference) return;
			// Types are needed for the ternary operator optimization
			TypeAnalysis.Run(context, method);
			
			AnalyseLabels(method);
			foreach(ILBlock block in method.GetSelfAndChildrenRecursive<ILBlock>()) {
				bool modified;
				do {
					modified = false;
					
					if (abortBeforeStep == ILAstOptimizationStep.SimplifyShortCircuit) return;
					modified |= block.RunOptimization(SimplifyShortCircuit);
					
					if (abortBeforeStep == ILAstOptimizationStep.SimplifyTernaryOperator) return;
					modified |= block.RunOptimization(SimplifyTernaryOperator);
					
					if (abortBeforeStep == ILAstOptimizationStep.SimplifyNullCoalescing) return;
					modified |= block.RunOptimization(SimplifyNullCoalescing);
					
				} while(modified);
			}
			
			ILInlining inlining2 = new ILInlining(method);
			inlining2.InlineAllVariables();
			inlining2.CopyPropagation();
			
			foreach(ILBlock block in method.GetSelfAndChildrenRecursive<ILBlock>()) {
				
				// Intentionaly outside the while(modifed) loop,
				// I will put it there later after more testing
				
				bool modified = false;
				
				if (abortBeforeStep == ILAstOptimizationStep.TransformDecimalCtorToConstant) return;
				modified |= block.RunOptimization(TransformDecimalCtorToConstant);
				
				if (abortBeforeStep == ILAstOptimizationStep.SimplifyLdObjAndStObj) return;
				modified |= block.RunOptimization(SimplifyLdObjAndStObj);
				
				if (abortBeforeStep == ILAstOptimizationStep.TransformArrayInitializers) return;
				modified |= block.RunOptimization(Initializers.TransformArrayInitializers);
				modified |= block.RunOptimization(Initializers.TransformArrayInitializers);
				
				if (abortBeforeStep == ILAstOptimizationStep.TransformCollectionInitializers) return;
				modified |= block.RunOptimization(Initializers.TransformCollectionInitializers);
				
				if (abortBeforeStep == ILAstOptimizationStep.MakeAssignmentExpression) return;
				modified |= block.RunOptimization(MakeAssignmentExpression);
				
				if (abortBeforeStep == ILAstOptimizationStep.InlineVariables2) return;
				modified |= new ILInlining(method).InlineAllInBlock(block);
			}
			
			if (abortBeforeStep == ILAstOptimizationStep.FindLoops) return;
			foreach(ILBlock block in method.GetSelfAndChildrenRecursive<ILBlock>()) {
				new LoopsAndConditions(context).FindLoops(block);
			}
			
			if (abortBeforeStep == ILAstOptimizationStep.FindConditions) return;
			foreach(ILBlock block in method.GetSelfAndChildrenRecursive<ILBlock>()) {
				new LoopsAndConditions(context).FindConditions(block);
			}
			
			if (abortBeforeStep == ILAstOptimizationStep.FlattenNestedMovableBlocks) return;
			FlattenBasicBlocks(method);
			
			if (abortBeforeStep == ILAstOptimizationStep.RemoveRedundantCode2) return;
			RemoveRedundantCode(method);
			
			if (abortBeforeStep == ILAstOptimizationStep.GotoRemoval) return;
			new GotoRemoval().RemoveGotos(method);
			
			if (abortBeforeStep == ILAstOptimizationStep.DuplicateReturns) return;
			DuplicateReturnStatements(method);
			
			if (abortBeforeStep == ILAstOptimizationStep.ReduceIfNesting) return;
			ReduceIfNesting(method);
			
			if (abortBeforeStep == ILAstOptimizationStep.InlineVariables3) return;
			// The 2nd inlining pass is necessary because DuplicateReturns and the introduction of ternary operators
			// open up additional inlining possibilities.
			new ILInlining(method).InlineAllVariables();
			
			if (abortBeforeStep == ILAstOptimizationStep.CachedDelegateInitialization) return;
			foreach(ILBlock block in method.GetSelfAndChildrenRecursive<ILBlock>()) {
				for (int i = 0; i < block.Body.Count; i++) {
					// TODO: Move before loops
					CachedDelegateInitialization(block, ref i);
				}
			}
			
			if (abortBeforeStep == ILAstOptimizationStep.IntroduceFixedStatements) return;
			foreach(ILBlock block in method.GetSelfAndChildrenRecursive<ILBlock>()) {
				for (int i = block.Body.Count - 1; i >= 0; i--) {
					// TODO: Move before loops
					if (i < block.Body.Count)
						IntroduceFixedStatements(block.Body, i);
				}
			}
			foreach(ILBlock block in method.GetSelfAndChildrenRecursive<ILBlock>()) {
				for (int i = block.Body.Count - 1; i >= 0; i--) {
					// TODO: Move before loops
					if (i < block.Body.Count)
						IntroduceFixedStatements(block.Body, i);
				}
			}
			
			if (abortBeforeStep == ILAstOptimizationStep.TypeInference2) return;
			TypeAnalysis.Reset(method);
			TypeAnalysis.Run(context, method);
			
			if (abortBeforeStep == ILAstOptimizationStep.RemoveRedundantCode3) return;
			GotoRemoval.RemoveRedundantCode(method);
			
			// ReportUnassignedILRanges(method);
		}
		
		/// <summary>
		/// Removes redundatant Br, Nop, Dup, Pop
		/// </summary>
		/// <param name="method"></param>
		void RemoveRedundantCode(ILBlock method)
		{
			Dictionary<ILLabel, int> labelRefCount = new Dictionary<ILLabel, int>();
			foreach (ILLabel target in method.GetSelfAndChildrenRecursive<ILExpression>(e => e.IsBranch()).SelectMany(e => e.GetBranchTargets())) {
				labelRefCount[target] = labelRefCount.GetOrDefault(target) + 1;
			}
			
			foreach(ILBlock block in method.GetSelfAndChildrenRecursive<ILBlock>()) {
				List<ILNode> body = block.Body;
				List<ILNode> newBody = new List<ILNode>(body.Count);
				for (int i = 0; i < body.Count; i++) {
					ILLabel target;
					ILExpression popExpr;
					if (body[i].Match(ILCode.Br, out target) && i+1 < body.Count && body[i+1] == target) {
						// Ignore the branch
						if (labelRefCount[target] == 1)
							i++;  // Ignore the label as well
					} else if (body[i].Match(ILCode.Nop)){
						// Ignore nop
					} else if (body[i].Match(ILCode.Pop, out popExpr)) {
						ILVariable v;
						if (!popExpr.Match(ILCode.Ldloc, out v))
							throw new Exception("Pop should have just ldloc at this stage");
						// Best effort to move the ILRange to previous statement
						ILVariable prevVar;
						ILExpression prevExpr;
						if (i - 1 >= 0 && body[i - 1].Match(ILCode.Stloc, out prevVar, out prevExpr) && prevVar == v)
							prevExpr.ILRanges.AddRange(((ILExpression)body[i]).ILRanges);
						// Ignore pop
					} else {
						newBody.Add(body[i]);
					}
				}
				block.Body = newBody;
			}
			
			// 'dup' removal
			foreach (ILExpression expr in method.GetSelfAndChildrenRecursive<ILExpression>()) {
				for (int i = 0; i < expr.Arguments.Count; i++) {
					ILExpression child;
					if (expr.Arguments[i].Match(ILCode.Dup, out child)) {
						child.ILRanges.AddRange(expr.Arguments[i].ILRanges);
						expr.Arguments[i] = child;
					}
				}
			}
		}
		
		/// <summary>
		/// Reduces the branch codes to just br and brtrue.
		/// Moves ILRanges to the branch argument
		/// </summary>
		void ReduceBranchInstructionSet(ILBlock block)
		{
			for (int i = 0; i < block.Body.Count; i++) {
				ILExpression expr = block.Body[i] as ILExpression;
				if (expr != null && expr.Prefixes == null) {
					switch(expr.Code) {
						case ILCode.Switch:
						case ILCode.Brtrue:
							expr.Arguments.Single().ILRanges.AddRange(expr.ILRanges);
							expr.ILRanges.Clear();
							continue;
							case ILCode.__Brfalse:  block.Body[i] = new ILExpression(ILCode.Brtrue, expr.Operand, new ILExpression(ILCode.LogicNot, null, expr.Arguments.Single())); break;
							case ILCode.__Beq:      block.Body[i] = new ILExpression(ILCode.Brtrue, expr.Operand, new ILExpression(ILCode.Ceq, null, expr.Arguments)); break;
							case ILCode.__Bne_Un:   block.Body[i] = new ILExpression(ILCode.Brtrue, expr.Operand, new ILExpression(ILCode.LogicNot, null, new ILExpression(ILCode.Ceq, null, expr.Arguments))); break;
							case ILCode.__Bgt:      block.Body[i] = new ILExpression(ILCode.Brtrue, expr.Operand, new ILExpression(ILCode.Cgt, null, expr.Arguments)); break;
							case ILCode.__Bgt_Un:   block.Body[i] = new ILExpression(ILCode.Brtrue, expr.Operand, new ILExpression(ILCode.Cgt_Un, null, expr.Arguments)); break;
							case ILCode.__Ble:      block.Body[i] = new ILExpression(ILCode.Brtrue, expr.Operand, new ILExpression(ILCode.LogicNot, null, new ILExpression(ILCode.Cgt, null, expr.Arguments))); break;
							case ILCode.__Ble_Un:   block.Body[i] = new ILExpression(ILCode.Brtrue, expr.Operand, new ILExpression(ILCode.LogicNot, null, new ILExpression(ILCode.Cgt_Un, null, expr.Arguments))); break;
							case ILCode.__Blt:      block.Body[i] = new ILExpression(ILCode.Brtrue, expr.Operand, new ILExpression(ILCode.Clt, null, expr.Arguments)); break;
							case ILCode.__Blt_Un:   block.Body[i] = new ILExpression(ILCode.Brtrue, expr.Operand, new ILExpression(ILCode.Clt_Un, null, expr.Arguments)); break;
							case ILCode.__Bge:	    block.Body[i] = new ILExpression(ILCode.Brtrue, expr.Operand, new ILExpression(ILCode.LogicNot, null, new ILExpression(ILCode.Clt, null, expr.Arguments))); break;
							case ILCode.__Bge_Un:   block.Body[i] = new ILExpression(ILCode.Brtrue, expr.Operand, new ILExpression(ILCode.LogicNot, null, new ILExpression(ILCode.Clt_Un, null, expr.Arguments))); break;
						default:
							continue;
					}
					((ILExpression)block.Body[i]).Arguments.Single().ILRanges.AddRange(expr.ILRanges);
				}
			}
		}
		
		/// <summary>
		/// Group input into a set of blocks that can be later arbitraliby schufled.
		/// The method adds necessary branches to make control flow between blocks
		/// explicit and thus order independent.
		/// </summary>
		void SplitToBasicBlocks(ILBlock block)
		{
			List<ILNode> basicBlocks = new List<ILNode>();
			
			ILBasicBlock basicBlock = new ILBasicBlock() {
				EntryLabel = block.Body.FirstOrDefault() as ILLabel ?? new ILLabel() { Name = "Block_" + (nextLabelIndex++) }
			};
			basicBlocks.Add(basicBlock);
			block.EntryGoto = new ILExpression(ILCode.Br, basicBlock.EntryLabel);
			
			if (block.Body.Count > 0) {
				if (block.Body[0] != basicBlock.EntryLabel)
					basicBlock.Body.Add(block.Body[0]);
				
				for (int i = 1; i < block.Body.Count; i++) {
					ILNode lastNode = block.Body[i - 1];
					ILNode currNode = block.Body[i];
					
					// Start a new basic block if necessary
					if (currNode is ILLabel ||
					    lastNode is ILTryCatchBlock ||
					    currNode is ILTryCatchBlock ||
					    (lastNode is ILExpression && ((ILExpression)lastNode).IsBranch()))
					{
						// Try to reuse the label
						ILLabel label = currNode is ILLabel ? ((ILLabel)currNode) : new ILLabel() { Name = "Block_" + (nextLabelIndex++) };
						
						// Terminate the last block
						if (lastNode.CanFallThough()) {
							// Explicit branch from one block to other
							basicBlock.FallthoughGoto = new ILExpression(ILCode.Br, label);
						} else if (lastNode.Match(ILCode.Br)) {
							// Reuse the existing goto as FallthoughGoto
							basicBlock.FallthoughGoto = (ILExpression)lastNode;
							basicBlock.Body.RemoveAt(basicBlock.Body.Count - 1);
						}
						
						// Start the new block						
						basicBlock = new ILBasicBlock();
						basicBlocks.Add(basicBlock);
						basicBlock.EntryLabel = label;
					}
					
					// Add the node to the basic block
					if (currNode != basicBlock.EntryLabel) {
						basicBlock.Body.Add(currNode);
					}
				}
			}
			
			block.Body = basicBlocks;
			return;
		}
		
		Dictionary<ILLabel, int> labelGlobalRefCount;
		Dictionary<ILLabel, ILBasicBlock> labelToBasicBlock;
		
		void AnalyseLabels(ILBlock method)
		{
			labelGlobalRefCount = new Dictionary<ILLabel, int>();
			foreach(ILLabel target in method.GetSelfAndChildrenRecursive<ILExpression>(e => e.IsBranch()).SelectMany(e => e.GetBranchTargets())) {
				if (!labelGlobalRefCount.ContainsKey(target))
					labelGlobalRefCount[target] = 0;
				labelGlobalRefCount[target]++;
			}
			
			labelToBasicBlock = new Dictionary<ILLabel, ILBasicBlock>();
			foreach(ILBasicBlock bb in method.GetSelfAndChildrenRecursive<ILBasicBlock>()) {
				foreach(ILLabel label in bb.GetChildren().OfType<ILLabel>()) {
					labelToBasicBlock[label] = bb;
				}
			}
		}
		
		bool SimplifyTernaryOperator(List<ILNode> body, ILBasicBlock head, int pos)
		{
			Debug.Assert(body.Contains(head));
			
			ILExpression condExpr;
			ILLabel trueLabel;
			ILLabel falseLabel;
			ILVariable trueLocVar = null;
			ILExpression trueExpr;
			ILLabel trueFall;
			ILVariable falseLocVar = null;
			ILExpression falseExpr;
			ILLabel falseFall;
			object unused;
			
			if (head.MatchLast(ILCode.Brtrue, out trueLabel, out condExpr, out falseLabel) &&
			    labelGlobalRefCount[trueLabel] == 1 &&
			    labelGlobalRefCount[falseLabel] == 1 &&
			    ((labelToBasicBlock[trueLabel].MatchSingle(ILCode.Stloc, out trueLocVar, out trueExpr, out trueFall) &&
			      labelToBasicBlock[falseLabel].MatchSingle(ILCode.Stloc, out falseLocVar, out falseExpr, out falseFall)) ||
			     (labelToBasicBlock[trueLabel].MatchSingle(ILCode.Ret, out unused, out trueExpr, out trueFall) &&
			      labelToBasicBlock[falseLabel].MatchSingle(ILCode.Ret, out unused, out falseExpr, out falseFall))) &&
			    trueLocVar == falseLocVar &&
			    trueFall == falseFall &&
			    body.Contains(labelToBasicBlock[trueLabel]) &&
			    body.Contains(labelToBasicBlock[falseLabel])
			   )
			{
				ILCode opCode = trueLocVar != null ? ILCode.Stloc : ILCode.Ret;
				TypeReference retType = trueLocVar != null ? trueLocVar.Type : this.context.CurrentMethod.ReturnType;
				int leftBoolVal;
				int rightBoolVal;
				ILExpression newExpr;
				// a ? true:false  is equivalent to  a
				// a ? false:true  is equivalent to  !a
				// a ? true : b    is equivalent to  a || b
				// a ? b : true    is equivalent to  !a || b
				// a ? b : false   is equivalent to  a && b
				// a ? false : b   is equivalent to  !a && b
				if (retType == typeSystem.Boolean &&
				    trueExpr.Match(ILCode.Ldc_I4, out leftBoolVal) &&
				    falseExpr.Match(ILCode.Ldc_I4, out rightBoolVal) &&
				    ((leftBoolVal != 0 && rightBoolVal == 0) || (leftBoolVal == 0 && rightBoolVal != 0))
				   )
				{
					// It can be expressed as trivilal expression
					if (leftBoolVal != 0) {
						newExpr = condExpr;
					} else {
						newExpr = new ILExpression(ILCode.LogicNot, null, condExpr);
					}
				} else if (retType == typeSystem.Boolean && trueExpr.Match(ILCode.Ldc_I4, out leftBoolVal)) {
					// It can be expressed as logical expression
					if (leftBoolVal != 0) {
						newExpr = MakeLeftAssociativeShortCircuit(ILCode.LogicOr, condExpr, falseExpr);
					} else {
						newExpr = MakeLeftAssociativeShortCircuit(ILCode.LogicAnd, new ILExpression(ILCode.LogicNot, null, condExpr), falseExpr);
					}
				} else if (retType == typeSystem.Boolean && falseExpr.Match(ILCode.Ldc_I4, out rightBoolVal)) {
					// It can be expressed as logical expression
					if (rightBoolVal != 0) {
						newExpr = MakeLeftAssociativeShortCircuit(ILCode.LogicOr, new ILExpression(ILCode.LogicNot, null, condExpr), trueExpr);
					} else {
						newExpr = MakeLeftAssociativeShortCircuit(ILCode.LogicAnd, condExpr, trueExpr);
					}
				} else {
					// Ternary operator tends to create long complicated return statements					
					if (opCode == ILCode.Ret)
						return false;
					
					// Create ternary expression
					newExpr = new ILExpression(ILCode.TernaryOp, null, condExpr, trueExpr, falseExpr);
				}
				
				head.Body[head.Body.Count - 1] = new ILExpression(opCode, trueLocVar, newExpr);
				head.FallthoughGoto = trueFall != null ? new ILExpression(ILCode.Br, trueFall) : null;
				
				// Remove the old basic blocks
				foreach(ILLabel deleteLabel in new [] { trueLabel, falseLabel }) {
					body.RemoveOrThrow(labelToBasicBlock[deleteLabel]);
					labelGlobalRefCount.RemoveOrThrow(deleteLabel);
					labelToBasicBlock.RemoveOrThrow(deleteLabel);
				}
				
				return true;
			}
			return false;
		}
		
		bool SimplifyNullCoalescing(List<ILNode> body, ILBasicBlock head, int pos)
		{
			// ...
			// v = ldloc(leftVar)
			// brtrue(endBBLabel, ldloc(leftVar))
			// br(rightBBLabel)
			//
			// rightBBLabel:
			// v = rightExpr
			// br(endBBLabel)
			// ...
			// =>
			// ...
			// v = NullCoalescing(ldloc(leftVar), rightExpr)
			// br(endBBLabel)
				
			ILVariable v, v2;
			ILExpression leftExpr, leftExpr2;
			ILVariable leftVar;
			ILLabel endBBLabel, endBBLabel2;
			ILLabel rightBBLabel;
			ILBasicBlock rightBB;
			ILExpression rightExpr;
			if (head.Body.Count >= 2 &&
				head.Body[head.Body.Count - 2].Match(ILCode.Stloc, out v, out leftExpr) &&
			    leftExpr.Match(ILCode.Ldloc, out leftVar) &&
			    head.MatchLast(ILCode.Brtrue, out endBBLabel, out leftExpr2, out rightBBLabel) &&
			    leftExpr2.Match(ILCode.Ldloc, leftVar) &&
			    labelToBasicBlock.TryGetValue(rightBBLabel, out rightBB) &&
			    rightBB.MatchSingle(ILCode.Stloc, out v2, out rightExpr, out endBBLabel2) &&
			    v == v2 &&
			    endBBLabel == endBBLabel2 &&
			    labelGlobalRefCount.GetOrDefault(rightBBLabel) == 1 &&
			    body.Contains(rightBB)
			   )
			{
				head.Body[head.Body.Count - 2] = new ILExpression(ILCode.Stloc, v, new ILExpression(ILCode.NullCoalescing, null, leftExpr, rightExpr));
				head.Body.RemoveAt(head.Body.Count - 1);
				head.FallthoughGoto = new ILExpression(ILCode.Br, endBBLabel);
				
				body.RemoveOrThrow(labelToBasicBlock[rightBBLabel]);
				labelGlobalRefCount.RemoveOrThrow(rightBBLabel);
				labelToBasicBlock.RemoveOrThrow(rightBBLabel);
				return true;
			}
			return false;
		}
		
		bool SimplifyShortCircuit(List<ILNode> body, ILBasicBlock head, int pos)
		{
			Debug.Assert(body.Contains(head));
			
			ILExpression condExpr;
			ILLabel trueLabel;
			ILLabel falseLabel;
			if(head.MatchLast(ILCode.Brtrue, out trueLabel, out condExpr, out falseLabel)) {
				for (int pass = 0; pass < 2; pass++) {
					
					// On the second pass, swap labels and negate expression of the first branch
					// It is slightly ugly, but much better then copy-pasting this whole block
					ILLabel nextLabel   = (pass == 0) ? trueLabel  : falseLabel;
					ILLabel otherLablel = (pass == 0) ? falseLabel : trueLabel;
					bool    negate      = (pass == 1);
					
					ILBasicBlock nextBasicBlock = labelToBasicBlock[nextLabel];
					ILExpression nextCondExpr;
					ILLabel nextTrueLablel;
					ILLabel nextFalseLabel;
					if (body.Contains(nextBasicBlock) &&
					    nextBasicBlock != head &&
					    labelGlobalRefCount[nextBasicBlock.EntryLabel] == 1 &&
					    nextBasicBlock.MatchSingle(ILCode.Brtrue, out nextTrueLablel, out nextCondExpr, out nextFalseLabel) &&
					    (otherLablel == nextFalseLabel || otherLablel == nextTrueLablel))
					{
						// Create short cicuit branch
						ILExpression logicExpr;
						if (otherLablel == nextFalseLabel) {
							logicExpr = MakeLeftAssociativeShortCircuit(ILCode.LogicAnd, negate ? new ILExpression(ILCode.LogicNot, null, condExpr) : condExpr, nextCondExpr);
						} else {
							logicExpr = MakeLeftAssociativeShortCircuit(ILCode.LogicOr, negate ? condExpr : new ILExpression(ILCode.LogicNot, null, condExpr), nextCondExpr);
						}
						head.Body[head.Body.Count - 1] = new ILExpression(ILCode.Brtrue, nextTrueLablel, logicExpr);
						head.FallthoughGoto = new ILExpression(ILCode.Br, nextFalseLabel);
						
						// Remove the inlined branch from scope
						labelGlobalRefCount.RemoveOrThrow(nextBasicBlock.EntryLabel);
						labelToBasicBlock.RemoveOrThrow(nextBasicBlock.EntryLabel);
						body.RemoveOrThrow(nextBasicBlock);
						
						return true;
					}
				}
			}
			return false;
		}
		
		ILExpression MakeLeftAssociativeShortCircuit(ILCode code, ILExpression left, ILExpression right)
		{
			// Assuming that the inputs are already left associative
			if (right.Match(code)) {
				// Find the leftmost logical expression
				ILExpression current = right;
				while(current.Arguments[0].Match(code))
					current = current.Arguments[0];
				current.Arguments[0] = new ILExpression(code, null, left, current.Arguments[0]);
				return right;
			} else {
				return new ILExpression(code, null, left, right);
			}
		}
		
		void DuplicateReturnStatements(ILBlock method)
		{
			Dictionary<ILLabel, ILNode> nextSibling = new Dictionary<ILLabel, ILNode>();
			
			// Build navigation data
			foreach(ILBlock block in method.GetSelfAndChildrenRecursive<ILBlock>()) {
				for (int i = 0; i < block.Body.Count - 1; i++) {
					ILLabel curr = block.Body[i] as ILLabel;
					if (curr != null) {
						nextSibling[curr] = block.Body[i + 1];
					}
				}
			}
			
			// Duplicate returns
			foreach(ILBlock block in method.GetSelfAndChildrenRecursive<ILBlock>()) {
				for (int i = 0; i < block.Body.Count; i++) {
					ILLabel targetLabel;
					if (block.Body[i].Match(ILCode.Br, out targetLabel) || block.Body[i].Match(ILCode.Leave, out targetLabel)) {
						// Skip extra labels
						while(nextSibling.ContainsKey(targetLabel) && nextSibling[targetLabel] is ILLabel) {
							targetLabel = (ILLabel)nextSibling[targetLabel];
						}
						
						// Inline return statement
						ILNode target;
						List<ILExpression> retArgs;
						if (nextSibling.TryGetValue(targetLabel, out target)) {
							if (target.Match(ILCode.Ret, out retArgs)) {
								ILVariable locVar;
								object constValue;
								if (retArgs.Count == 0) {
									block.Body[i] = new ILExpression(ILCode.Ret, null);
								} else if (retArgs.Single().Match(ILCode.Ldloc, out locVar)) {
									block.Body[i] = new ILExpression(ILCode.Ret, null, new ILExpression(ILCode.Ldloc, locVar));
								} else if (retArgs.Single().Match(ILCode.Ldc_I4, out constValue)) {
									block.Body[i] = new ILExpression(ILCode.Ret, null, new ILExpression(ILCode.Ldc_I4, constValue));
								}
							}
						} else {
							if (method.Body.Count > 0 && method.Body.Last() == targetLabel) {
								// It exits the main method - so it is same as return;
								block.Body[i] = new ILExpression(ILCode.Ret, null);
							}
						}
					}
				}
			}
		}
		
		/// <summary>
		/// Flattens all nested basic blocks, except the the top level 'node' argument
		/// </summary>
		void FlattenBasicBlocks(ILNode node)
		{
			ILBlock block = node as ILBlock;
			if (block != null) {
				List<ILNode> flatBody = new List<ILNode>();
				foreach (ILNode child in block.GetChildren()) {
					FlattenBasicBlocks(child);
					if (child is ILBasicBlock) {
						flatBody.AddRange(child.GetChildren());
					} else {
						flatBody.Add(child);
					}
				}
				block.EntryGoto = null;
				block.Body = flatBody;
			} else if (node is ILExpression) {
				// Optimization - no need to check expressions
			} else if (node != null) {
				// Recursively find all ILBlocks
				foreach(ILNode child in node.GetChildren()) {
					FlattenBasicBlocks(child);
				}
			}
		}
		
		/// <summary>
		/// Reduce the nesting of conditions.
		/// It should be done on flat data that already had most gotos removed
		/// </summary>
		void ReduceIfNesting(ILNode node)
		{
			ILBlock block = node as ILBlock;
			if (block != null) {
				for (int i = 0; i < block.Body.Count; i++) {
					ILCondition cond = block.Body[i] as ILCondition;
					if (cond != null) {
						bool trueExits = cond.TrueBlock.Body.Count > 0 && !cond.TrueBlock.Body.Last().CanFallThough();
						bool falseExits = cond.FalseBlock.Body.Count > 0 && !cond.FalseBlock.Body.Last().CanFallThough();
						
						if (trueExits) {
							// Move the false block after the condition
							block.Body.InsertRange(i + 1, cond.FalseBlock.GetChildren());
							cond.FalseBlock = new ILBlock();
						} else if (falseExits) {
							// Move the true block after the condition
							block.Body.InsertRange(i + 1, cond.TrueBlock.GetChildren());
							cond.TrueBlock = new ILBlock();
						}
						
						// Eliminate empty true block
						if (!cond.TrueBlock.GetChildren().Any() && cond.FalseBlock.GetChildren().Any()) {
							// Swap bodies
							ILBlock tmp = cond.TrueBlock;
							cond.TrueBlock = cond.FalseBlock;
							cond.FalseBlock = tmp;
							cond.Condition = new ILExpression(ILCode.LogicNot, null, cond.Condition);
						}
					}
				}
			}
			
			// We are changing the number of blocks so we use plain old recursion to get all blocks
			foreach(ILNode child in node.GetChildren()) {
				if (child != null && !(child is ILExpression))
					ReduceIfNesting(child);
			}
		}
		
		void ReportUnassignedILRanges(ILBlock method)
		{
			var unassigned = ILRange.Invert(method.GetSelfAndChildrenRecursive<ILExpression>().SelectMany(e => e.ILRanges), context.CurrentMethod.Body.CodeSize).ToList();
			if (unassigned.Count > 0)
				Debug.WriteLine(string.Format("Unassigned ILRanges for {0}.{1}: {2}", this.context.CurrentMethod.DeclaringType.Name, this.context.CurrentMethod.Name, string.Join(", ", unassigned.Select(r => r.ToString()))));
		}
	}
	
	public static class ILAstOptimizerExtensionMethods
	{
		/// <summary>
		/// Perform one pass of a given optimization on this block.
		/// This block must consist of only basicblocks.
		/// </summary>
		public static bool RunOptimization(this ILBlock block, Func<List<ILNode>, ILBasicBlock, int, bool> optimization)
		{
			bool modified = false;
			List<ILNode> body = block.Body;
			for (int i = body.Count - 1; i >= 0; i--) {
				if (i < body.Count && optimization(body, (ILBasicBlock)body[i], i)) {
					modified = true;
				}
			}
			return modified;
		}
		
		public static bool RunOptimization(this ILBlock block, Func<List<ILNode>, ILExpression, int, bool> optimization)
		{
			bool modified = false;
			foreach (ILBasicBlock bb in block.Body) {
				for (int i = bb.Body.Count - 1; i >= 0; i--) {
					ILExpression expr = bb.Body.ElementAtOrDefault(i) as ILExpression;
					if (expr != null && optimization(bb.Body, expr, i)) {
						modified = true;
					}
				}
			}
			return modified;
		}
		
		public static bool CanFallThough(this ILNode node)
		{
			ILExpression expr = node as ILExpression;
			if (expr != null) {
				return expr.Code.CanFallThough();
			}
			return true;
		}
		
		/// <summary>
		/// The expression has no effect on the program and can be removed 
		/// if its return value is not needed.
		/// </summary>
		public static bool HasNoSideEffects(this ILExpression expr)
		{
			// Remember that if expression can throw an exception, it is a side effect
			
			switch(expr.Code) {
				case ILCode.Ldloc:
				case ILCode.Ldloca:
				case ILCode.Ldstr:
				case ILCode.Ldnull:
				case ILCode.Ldc_I4:
				case ILCode.Ldc_I8:
				case ILCode.Ldc_R4:
				case ILCode.Ldc_R8:
					return true;
				default:
					return false;
			}
		}
		
		public static bool IsStoreToArray(this ILCode code)
		{
			switch (code) {
				case ILCode.Stelem_Any:
				case ILCode.Stelem_I:
				case ILCode.Stelem_I1:
				case ILCode.Stelem_I2:
				case ILCode.Stelem_I4:
				case ILCode.Stelem_I8:
				case ILCode.Stelem_R4:
				case ILCode.Stelem_R8:
				case ILCode.Stelem_Ref:
					return true;
				default:
					return false;
			}
		}
		
		/// <summary>
		/// Can the expression be used as a statement in C#?
		/// </summary>
		public static bool CanBeExpressionStatement(this ILExpression expr)
		{
			switch(expr.Code) {
				case ILCode.Call:
				case ILCode.Callvirt:
					// property getters can't be expression statements, but all other method calls can be
					MethodReference mr = (MethodReference)expr.Operand;
					return !mr.Name.StartsWith("get_", StringComparison.Ordinal);
				case ILCode.Newobj:
				case ILCode.Newarr:
					return true;
				default:
					return false;
			}
		}
		
		public static V GetOrDefault<K,V>(this Dictionary<K, V> dict, K key)
		{
			V ret;
			dict.TryGetValue(key, out ret);
			return ret;
		}
		
		public static void RemoveOrThrow<T>(this ICollection<T> collection, T item)
		{
			if (!collection.Remove(item))
				throw new Exception("The item was not found in the collection");
		}
		
		public static void RemoveOrThrow<K,V>(this Dictionary<K,V> collection, K key)
		{
			if (!collection.Remove(key))
				throw new Exception("The key was not found in the dictionary");
		}
	}
}
