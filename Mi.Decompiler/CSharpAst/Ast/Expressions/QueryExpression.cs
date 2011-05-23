// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under MIT X11 license (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;
using System.Linq;

namespace Mi.CSharp
{
    using Mi.NRefactory.PatternMatching;
    
    public class QueryExpression : Expression
	{
		public static readonly Role<QueryClause> ClauseRole = new Role<QueryClause>("Clause");
		
		#region Null
		public new static readonly QueryExpression Null = new NullQueryExpression ();
		
		sealed class NullQueryExpression : QueryExpression
		{
			public override bool IsNull {
				get {
					return true;
				}
			}
			
			public override S AcceptVisitor<T, S> (IAstVisitor<T, S> visitor, T data)
			{
				return default (S);
			}
			
			protected internal override bool DoMatch(AstNode other, Match match)
			{
				return other == null || other.IsNull;
			}
		}
		#endregion
		
		public AstNodeCollection<QueryClause> Clauses {
			get { return GetChildrenByRole(ClauseRole); }
		}
		
		public override S AcceptVisitor<T, S>(IAstVisitor<T, S> visitor, T data)
		{
			return visitor.VisitQueryExpression (this, data);
		}
		
		protected internal override bool DoMatch(AstNode other, Match match)
		{
			QueryExpression o = other as QueryExpression;
			return o != null && !o.IsNull && this.Clauses.DoMatch(o.Clauses, match);
		}
	}
	
	public abstract class QueryClause : AstNode
	{
		public override NodeType NodeType {
			get { return NodeType.QueryClause; }
		}
	}
	
	/// <summary>
	/// Represents a query continuation.
	/// "(from .. select ..) into Identifier" or "(from .. group .. by ..) into Identifier"
	/// Note that "join .. into .." is not a query continuation!
	/// 
	/// This is always the first(!!) clause in a query expression.
	/// The tree for "from a in b select c into d select e" looks like this:
	/// new QueryExpression {
	/// 	new QueryContinuationClause {
	/// 		PrecedingQuery = new QueryExpression {
	/// 			new QueryFromClause(a in b),
	/// 			new QuerySelectClause(c)
	/// 		},
	/// 		Identifier = d
	/// 	},
	/// 	new QuerySelectClause(e)
	/// }
	/// </summary>
	public class QueryContinuationClause : QueryClause
	{
		public static readonly Role<QueryExpression> PrecedingQueryRole = new Role<QueryExpression>("PrecedingQuery", QueryExpression.Null);
		public static readonly Role<CSharpTokenNode> IntoKeywordRole = Roles.Keyword;
		
		public QueryExpression PrecedingQuery {
			get { return GetChildByRole(PrecedingQueryRole); }
			set { SetChildByRole(PrecedingQueryRole, value); }
		}
		
		public string Identifier {
			get {
				return GetChildByRole (Roles.Identifier).Name;
			}
			set {
				SetChildByRole(Roles.Identifier, new Identifier(value, AstLocation.Empty));
			}
		}
		
		public override S AcceptVisitor<T, S> (IAstVisitor<T, S> visitor, T data)
		{
			return visitor.VisitQueryContinuationClause (this, data);
		}
		
		protected internal override bool DoMatch(AstNode other, Match match)
		{
			QueryContinuationClause o = other as QueryContinuationClause;
			return o != null && MatchString(this.Identifier, o.Identifier) && this.PrecedingQuery.DoMatch(o.PrecedingQuery, match);
		}
	}
	
	public class QueryFromClause : QueryClause
	{
		public static readonly Role<CSharpTokenNode> FromKeywordRole = Roles.Keyword;
		public static readonly Role<CSharpTokenNode> InKeywordRole = Roles.InKeyword;
		
		public AstType Type {
			get { return GetChildByRole (Roles.Type); }
			set { SetChildByRole (Roles.Type, value); }
		}
		
		public string Identifier {
			get {
				return GetChildByRole (Roles.Identifier).Name;
			}
			set {
				SetChildByRole(Roles.Identifier, new Identifier(value, AstLocation.Empty));
			}
		}
		
		public Expression Expression {
			get { return GetChildByRole (Roles.Expression); }
			set { SetChildByRole (Roles.Expression, value); }
		}
		
		public override S AcceptVisitor<T, S> (IAstVisitor<T, S> visitor, T data)
		{
			return visitor.VisitQueryFromClause (this, data);
		}
		
		protected internal override bool DoMatch(AstNode other, Match match)
		{
			QueryFromClause o = other as QueryFromClause;
			return o != null && this.Type.DoMatch(o.Type, match) && MatchString(this.Identifier, o.Identifier)
				&& this.Expression.DoMatch(o.Expression, match);
		}
	}
	
	public class QueryLetClause : QueryClause
	{
		public CSharpTokenNode LetKeyword {
			get { return GetChildByRole(Roles.Keyword); }
		}
		
		public string Identifier {
			get {
				return GetChildByRole(Roles.Identifier).Name;
			}
			set {
				SetChildByRole(Roles.Identifier, new Identifier(value, AstLocation.Empty));
			}
		}
		
		public CSharpTokenNode AssignToken {
			get { return GetChildByRole(Roles.Assign); }
		}
		
		public Expression Expression {
			get { return GetChildByRole(Roles.Expression); }
			set { SetChildByRole(Roles.Expression, value); }
		}
		
		public override S AcceptVisitor<T, S> (IAstVisitor<T, S> visitor, T data)
		{
			return visitor.VisitQueryLetClause (this, data);
		}
		
		protected internal override bool DoMatch(AstNode other, Match match)
		{
			QueryLetClause o = other as QueryLetClause;
			return o != null && MatchString(this.Identifier, o.Identifier) && this.Expression.DoMatch(o.Expression, match);
		}
	}
	
	
	public class QueryWhereClause : QueryClause
	{
		public CSharpTokenNode WhereKeyword {
			get { return GetChildByRole (Roles.Keyword); }
		}
		
		public Expression Condition {
			get { return GetChildByRole (Roles.Condition); }
			set { SetChildByRole (Roles.Condition, value); }
		}
		
		public override S AcceptVisitor<T, S> (IAstVisitor<T, S> visitor, T data)
		{
			return visitor.VisitQueryWhereClause (this, data);
		}
		
		protected internal override bool DoMatch(AstNode other, Match match)
		{
			QueryWhereClause o = other as QueryWhereClause;
			return o != null && this.Condition.DoMatch(o.Condition, match);
		}
	}
	
	/// <summary>
	/// Represents a join or group join clause.
	/// </summary>
	public class QueryJoinClause : QueryClause
	{
		public static readonly Role<CSharpTokenNode> JoinKeywordRole = Roles.Keyword;
		public static readonly Role<AstType> TypeRole = Roles.Type;
		public static readonly Role<Identifier> JoinIdentifierRole = Roles.Identifier;
		public static readonly Role<CSharpTokenNode> InKeywordRole = Roles.InKeyword;
		public static readonly Role<Expression> InExpressionRole = Roles.Expression;
		public static readonly Role<CSharpTokenNode> OnKeywordRole = new Role<CSharpTokenNode>("OnKeyword", CSharpTokenNode.Null);
		public static readonly Role<Expression> OnExpressionRole = new Role<Expression>("OnExpression", Expression.Null);
		public static readonly Role<CSharpTokenNode> EqualsKeywordRole = new Role<CSharpTokenNode>("EqualsKeyword", CSharpTokenNode.Null);
		public static readonly Role<Expression> EqualsExpressionRole = new Role<Expression>("EqualsExpression", Expression.Null);
		public static readonly Role<CSharpTokenNode> IntoKeywordRole = new Role<CSharpTokenNode>("IntoKeyword", CSharpTokenNode.Null);
		public static readonly Role<Identifier> IntoIdentifierRole = new Role<Identifier>("IntoIdentifier", Identifier.Null);
		
		public bool IsGroupJoin {
			get { return !string.IsNullOrEmpty(this.IntoIdentifier); }
		}
		
		public CSharpTokenNode JoinKeyword {
			get { return GetChildByRole (JoinKeywordRole); }
		}
		
		public AstType Type {
			get { return GetChildByRole (TypeRole); }
			set { SetChildByRole (TypeRole, value); }
		}
		
		public string JoinIdentifier {
			get {
				return GetChildByRole(JoinIdentifierRole).Name;
			}
			set {
				SetChildByRole(JoinIdentifierRole, new Identifier(value, AstLocation.Empty));
			}
		}
		
		public CSharpTokenNode InKeyword {
			get { return GetChildByRole (InKeywordRole); }
		}
		
		public Expression InExpression {
			get { return GetChildByRole (InExpressionRole); }
			set { SetChildByRole (InExpressionRole, value); }
		}
		
		public CSharpTokenNode OnKeyword {
			get { return GetChildByRole (OnKeywordRole); }
		}
		
		public Expression OnExpression {
			get { return GetChildByRole (OnExpressionRole); }
			set { SetChildByRole (OnExpressionRole, value); }
		}
		
		public CSharpTokenNode EqualsKeyword {
			get { return GetChildByRole (EqualsKeywordRole); }
		}
		
		public Expression EqualsExpression {
			get { return GetChildByRole (EqualsExpressionRole); }
			set { SetChildByRole (EqualsExpressionRole, value); }
		}
		
		public CSharpTokenNode IntoKeyword {
			get { return GetChildByRole (IntoKeywordRole); }
		}
		
		public string IntoIdentifier {
			get {
				return GetChildByRole (IntoIdentifierRole).Name;
			}
			set {
				SetChildByRole(IntoIdentifierRole, new Identifier(value, AstLocation.Empty));
			}
		}
		
		public override S AcceptVisitor<T, S> (IAstVisitor<T, S> visitor, T data)
		{
			return visitor.VisitQueryJoinClause (this, data);
		}
		
		protected internal override bool DoMatch(AstNode other, Match match)
		{
			QueryJoinClause o = other as QueryJoinClause;
			return o != null && this.IsGroupJoin == o.IsGroupJoin
				&& this.Type.DoMatch(o.Type, match) && MatchString(this.JoinIdentifier, o.JoinIdentifier)
				&& this.InExpression.DoMatch(o.InExpression, match) && this.OnExpression.DoMatch(o.OnExpression, match)
				&& this.EqualsExpression.DoMatch(o.EqualsExpression, match)
				&& MatchString(this.IntoIdentifier, o.IntoIdentifier);
		}
	}
	
	public class QueryOrderClause : QueryClause
	{
		public static readonly Role<QueryOrdering> OrderingRole = new Role<QueryOrdering>("Ordering");
		
		public CSharpTokenNode Keyword {
			get { return GetChildByRole (Roles.Keyword); }
		}
		
		public AstNodeCollection<QueryOrdering> Orderings {
			get { return GetChildrenByRole (OrderingRole); }
		}
		
		public override S AcceptVisitor<T, S> (IAstVisitor<T, S> visitor, T data)
		{
			return visitor.VisitQueryOrderClause (this, data);
		}
		
		protected internal override bool DoMatch(AstNode other, Match match)
		{
			QueryOrderClause o = other as QueryOrderClause;
			return o != null && this.Orderings.DoMatch(o.Orderings, match);
		}
	}
	
	public class QueryOrdering : AstNode
	{
		public override NodeType NodeType {
			get { return NodeType.Unknown; }
		}
		
		public Expression Expression {
			get { return GetChildByRole (Roles.Expression); }
			set { SetChildByRole (Roles.Expression, value); }
		}
		
		public QueryOrderingDirection Direction {
			get;
			set;
		}
		
		public CSharpTokenNode DirectionToken {
			get { return GetChildByRole (Roles.Keyword); }
		}
		
		public override S AcceptVisitor<T, S> (IAstVisitor<T, S> visitor, T data)
		{
			return visitor.VisitQueryOrdering (this, data);
		}
		
		protected internal override bool DoMatch(AstNode other, Match match)
		{
			QueryOrdering o = other as QueryOrdering;
			return o != null && this.Direction == o.Direction && this.Expression.DoMatch(o.Expression, match);
		}
	}
	
	public enum QueryOrderingDirection
	{
		None,
		Ascending,
		Descending
	}
	
	public class QuerySelectClause : QueryClause
	{
		public CSharpTokenNode SelectKeyword {
			get { return GetChildByRole (Roles.Keyword); }
		}
		
		public Expression Expression {
			get { return GetChildByRole (Roles.Expression); }
			set { SetChildByRole (Roles.Expression, value); }
		}
		
		public override S AcceptVisitor<T, S> (IAstVisitor<T, S> visitor, T data)
		{
			return visitor.VisitQuerySelectClause (this, data);
		}
		
		protected internal override bool DoMatch(AstNode other, Match match)
		{
			QuerySelectClause o = other as QuerySelectClause;
			return o != null && this.Expression.DoMatch(o.Expression, match);
		}
	}
	
	public class QueryGroupClause : QueryClause
	{
		public static readonly Role<CSharpTokenNode> GroupKeywordRole = Roles.Keyword;
		public static readonly Role<Expression> ProjectionRole = new Role<Expression>("Projection", Expression.Null);
		public static readonly Role<CSharpTokenNode> ByKeywordRole = new Role<CSharpTokenNode>("ByKeyword", CSharpTokenNode.Null);
		public static readonly Role<Expression> KeyRole = new Role<Expression>("Key", Expression.Null);
		
		public CSharpTokenNode GroupKeyword {
			get { return GetChildByRole (GroupKeywordRole); }
		}
		
		public Expression Projection {
			get { return GetChildByRole (ProjectionRole); }
			set { SetChildByRole (ProjectionRole, value); }
		}
		
		public CSharpTokenNode ByKeyword {
			get { return GetChildByRole (ByKeywordRole); }
		}
		
		public Expression Key {
			get { return GetChildByRole (KeyRole); }
			set { SetChildByRole (KeyRole, value); }
		}
		
		public override S AcceptVisitor<T, S> (IAstVisitor<T, S> visitor, T data)
		{
			return visitor.VisitQueryGroupClause (this, data);
		}
		
		protected internal override bool DoMatch(AstNode other, Match match)
		{
			QueryGroupClause o = other as QueryGroupClause;
			return o != null && this.Projection.DoMatch(o.Projection, match) && this.Key.DoMatch(o.Key, match);
		}
	}
}