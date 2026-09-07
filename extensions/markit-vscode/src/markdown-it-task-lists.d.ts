declare module "markdown-it-task-lists" {
  import type MarkdownIt from "markdown-it";

  interface TaskListOptions {
    enabled?: boolean;
    label?: boolean;
    labelAfter?: boolean;
  }

  function taskLists(markdown: MarkdownIt, options?: TaskListOptions): void;

  export default taskLists;
}
