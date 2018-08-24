using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class TreeNode<T>
{
    public T Data { get; set; }
    public TreeNode<T> Parent { get; set; }
    public List<TreeNode<T>> Children { get; set; }

    public Boolean IsRoot
    {
        get { return Parent == null; }
    }

    public Boolean IsLeaf
    {
        get { return Children.Count == 0; }
    }

    public int Level
    {
        get
        {
            if (this.IsRoot)
                return 0;

            return Parent.Level + 1;
        }
    }

    public TreeNode()
    {
        this.Children = new List<TreeNode<T>>();
    }

    public TreeNode(T data)
    {
        this.Data = data;
        this.Children = new List<TreeNode<T>>();
    }

    public TreeNode<T> AddChild(T child)
    {
        TreeNode<T> childNode = new TreeNode<T>(child) { Parent = this };
        this.Children.Add(childNode);

        return childNode;
    }
}